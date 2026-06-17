using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.Net;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using System.IO;
using System;
using System.Threading;

public class ChunkVisualizer : MonoBehaviour
{
    [Header("网络设置")]
    public int port = 10006; 
    
    [Header("可视化设置")]
    public float pointSize = 0.02f;
    public float axisLength = 0.1f; 
    public float lineWidth = 0.005f;
    
    [Header("材质设置")]
    public Material whiteMaterial;
    public Material redMaterial;   
    public Material greenMaterial; 
    public Material blueMaterial;
    
    [Header("交互设置")]
    public bool enablePointSelection = true;  // 是否启用点选择
    public Color selectedPointColor = Color.yellow;  // 选中点的颜色
    public Color hoverPointColor = Color.cyan;  // 悬停点的颜色
    
    // 记录选中和悬停状态（用于轨迹更新后恢复）
    private int currentSelectedPointIndex = -1;
    private int currentHoveredPointIndex = -1;
    
    private UdpClient server;
    private Thread receiveThread;
    private JsonSerializer serializer = new JsonSerializer();
    
    private TrajectoryData currentTrajectoryData;
    
    private GameObject trajectoryContainer;
    private List<GameObject> pointObjects = new List<GameObject>();
    private List<GameObject> lineObjects = new List<GameObject>();
    private List<GameObject> axisObjects = new List<GameObject>();

    [Header("Ghost gripper settings")]
    public string ghostGripperResourcePath = "Prefab/GhostGripper";
    public float ghostJawWidth = 0.085f;
    public float ghostAlpha = 0.35f;
    public float ghostScale = 0.5f;
    public Vector3 ghostRotationOffsetEuler = new Vector3(0f, 90f, 0f);
    public bool showActionChunk = true;
    public bool showGhostGrippers = true;

    private GameObject ghostGripperPrefab;
    private GameObject lastPointGhost;
    private GameObject selectedPointGhost;
    private int selectedGhostIndex = -1;
    private bool selectedGhostEditing = false;
    private List<Vector3> cachedPositions = new List<Vector3>();
    private List<Quaternion> cachedRotations = new List<Quaternion>();
    
    // Action chunk data: 6D pose (x,y,z,r,p,y)
    [DataContract]
    public class TrajectoryPoint
    {
        [DataMember]
        public float x { get; set; }
        [DataMember]
        public float y { get; set; }
        [DataMember]
        public float z { get; set; }
        [DataMember]
        public float roll { get; set; }
        [DataMember]
        public float pitch { get; set; }
        [DataMember]
        public float yaw { get; set; }
    }
    
    [DataContract]
    public class TrajectoryData
    {
        [DataMember]
        public List<TrajectoryPoint> points { get; set; }
        [DataMember]
        public float timestamp { get; set; }
    }
    
    void Start()
    {
        InitializeTrajectoryContainer();
        StartReceivingData();
    }

    void Update()
    {
        UpdateGhostGripperJawWidth();
    }
    
    void InitializeTrajectoryContainer()
    {
        // 创建轨迹容器并挂载到 Calibration 实例下
        trajectoryContainer = new GameObject("ChunkTrajectoryVisualization");
        
        if (Calibration.instance != null)
        {
            trajectoryContainer.transform.SetParent(Calibration.instance.transform);
            Debug.Log("ChunkVisualizer: Container attached to Calibration - visualization will follow calibration transform");
        }
        else
        {
            trajectoryContainer.transform.SetParent(this.transform);
            Debug.LogWarning("ChunkVisualizer: Calibration instance not found! Visualization will NOT be calibrated.");
        }
    }
    
    void StartReceivingData()
    {
        receiveThread = new Thread(ReceiveTrajectoryData);
        receiveThread.Start();
    }
    
    void ReceiveTrajectoryData()
    {
        server = new UdpClient(port);
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        
        while (true)
        {
            try
            {
                byte[] receiveBytes = server.Receive(ref remoteEndPoint);
                
                using (MemoryStream ms = new MemoryStream(receiveBytes))
                {
                    using (BsonReader reader = new BsonReader(ms))
                    {
                        currentTrajectoryData = serializer.Deserialize<TrajectoryData>(reader);
                    }
                }
                
                // 使用主线程调度器在主线程更新可视化
                UnityMainThreadDispatcher.Instance().Enqueue(() => UpdateTrajectoryVisualization());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ChunkVisualizer: Error receiving trajectory - {e.Message}");
            }
        }
    }

    // 更新轨迹可视化（主线程调用）
    void UpdateTrajectoryVisualization()
    {
        if (currentTrajectoryData == null || currentTrajectoryData.points == null) return;
        
        // 清除旧的可视化
        ClearVisualization();
        
        // 提取位置和旋转
        List<Vector3> positions = ExtractPositions(currentTrajectoryData.points);
        List<Quaternion> rotations = ExtractRotations(currentTrajectoryData.points);

        // 创建新的可视化
        CreateTrajectoryPoints(positions);
        CreateConnectionLines(positions);
        CreateCoordinateAxes(positions, rotations);
        CacheTrajectoryPoses(positions, rotations);
        UpdateLastPointGhost();
        UpdateSelectedGhostPose();
        UpdateGhostVisibility();

        SetActionChunkVisible(showActionChunk);
        SetGhostGrippersVisible(showGhostGrippers);
        
        // 轨迹更新后恢复选中和悬停状态
        RestorePointStates();
    }
    
    List<Vector3> ExtractPositions(List<TrajectoryPoint> points)
    {
        List<Vector3> positions = new List<Vector3>();
        
        for (int i = 0; i < points.Count; i++)
        {
            positions.Add(new Vector3(points[i].x, points[i].y, points[i].z));
        }
        
        return positions;
    }
    
    List<Quaternion> ExtractRotations(List<TrajectoryPoint> points)
    {
        List<Quaternion> rotations = new List<Quaternion>();
        
        for (int i = 0; i < points.Count; i++)
        {
            // 将欧拉角（roll, pitch, yaw）转换为四元数
            // 注意：Unity的Quaternion.Euler使用的顺序是 (pitch, yaw, roll) 对应 (X, Y, Z)
            rotations.Add(Quaternion.Euler(points[i].pitch, points[i].yaw, points[i].roll));
        }
        
        return rotations;
    }

    // 创建轨迹点（球体）
    void CreateTrajectoryPoints(List<Vector3> positions)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = $"ChunkPoint_{i}";
            point.transform.SetParent(trajectoryContainer.transform);
            point.transform.localPosition = positions[i];
            point.transform.localScale = Vector3.one * pointSize;
            
            Renderer renderer = point.GetComponent<Renderer>();
            renderer.material = whiteMaterial;
            
            // 如果启用点选择，保留Collider并添加TrajectoryPointData组件
            if (enablePointSelection)
            {
                // 保留SphereCollider用于碰撞检测
                SphereCollider collider = point.GetComponent<SphereCollider>();
                if (collider != null)
                {
                    collider.isTrigger = false;
                }
                
                // 添加TrajectoryPointData组件存储点的index和状态
                TrajectoryPointData pointData = point.AddComponent<TrajectoryPointData>();
                pointData.pointIndex = i;
                pointData.visualizer = this;
            }
            else
            {
                // 如果不启用点选择，删除Collider节省性能
                DestroyImmediate(point.GetComponent<Collider>());
            }
            
            pointObjects.Add(point);
        }
    }
    
    // 创建连接轨迹点的线段
    void CreateConnectionLines(List<Vector3> positions)
    {
        for (int i = 0; i < positions.Count - 1; i++)
        {
            GameObject line = CreateLine(positions[i], positions[i + 1], $"ChunkLine_{i}_{i + 1}", 0.5f, 1.0f);
            lineObjects.Add(line);
        }
    }
    
    // 创建每个轨迹点的坐标轴
    void CreateCoordinateAxes(List<Vector3> positions, List<Quaternion> rotations)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 localPosition = positions[i];  // 局部坐标
            Quaternion localRotation = rotations[i];  // 局部旋转
            
            // 关键修复：在局部空间中计算坐标轴方向
            // 由于 position 是局部坐标，rotation 也应该是局部旋转
            // 坐标轴方向应该在局部空间中计算
            Vector3 rightDirection = localRotation * Vector3.right;
            Vector3 upDirection = localRotation * Vector3.up;
            Vector3 forwardDirection = localRotation * Vector3.forward;
            
            // x轴 (红色)
            Vector3 xEnd = localPosition + rightDirection * axisLength;
            GameObject xAxis = CreateLine(localPosition, xEnd, $"ChunkXAxis_{i}", 0.3f, 0.5f);
            xAxis.GetComponent<Renderer>().material = redMaterial;
            axisObjects.Add(xAxis);
            
            // y轴 (绿色)
            Vector3 yEnd = localPosition + upDirection * axisLength;
            GameObject yAxis = CreateLine(localPosition, yEnd, $"ChunkYAxis_{i}", 0.3f, 0.5f);
            yAxis.GetComponent<Renderer>().material = greenMaterial;
            axisObjects.Add(yAxis);
            
            // z轴 (蓝色)
            Vector3 zEnd = localPosition + forwardDirection * axisLength;
            GameObject zAxis = CreateLine(localPosition, zEnd, $"ChunkZAxis_{i}", 0.3f, 0.5f);
            zAxis.GetComponent<Renderer>().material = blueMaterial;
            axisObjects.Add(zAxis);
        }
    }
    
    // 创建线段（使用圆柱体）
    GameObject CreateLine(Vector3 start, Vector3 end, string name, float thicknessFactor = 1.0f, float lengthFactor = 1.0f)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        line.name = name;
        line.transform.SetParent(trajectoryContainer.transform);
        
        Vector3 direction = end - start;
        float originalLength = direction.magnitude;
        
        // 应用长度系数
        float scaledLength = originalLength * lengthFactor;
        Vector3 scaledEnd = start + direction.normalized * scaledLength;
        Vector3 center = (start + scaledEnd) / 2f;
        
        // 使用 localPosition 和 localRotation，使线段相对于父对象（Calibration）
        line.transform.localPosition = center;
        line.transform.localRotation = Quaternion.LookRotation(direction);
        line.transform.Rotate(90, 0, 0);
        
        // 应用厚度系数
        float thickness = lineWidth * thicknessFactor;
        line.transform.localScale = new Vector3(thickness * 2f, scaledLength / 2f, thickness * 2f);
        
        Renderer renderer = line.GetComponent<Renderer>();
        renderer.material = whiteMaterial;
        DestroyImmediate(line.GetComponent<Collider>());
        
        return line;
    }
    
    void ClearVisualization()
    {
        if (trajectoryContainer != null)
        {
            // 直接销毁所有子对象
            foreach (GameObject point in pointObjects)
            {
                Destroy(point);
            }
            pointObjects.Clear();
            
            foreach (GameObject line in lineObjects)
            {
                Destroy(line);
            }
            lineObjects.Clear();
            
            foreach (GameObject axis in axisObjects)
            {
                Destroy(axis);
            }
            axisObjects.Clear();

        }
    }

    void CacheTrajectoryPoses(List<Vector3> positions, List<Quaternion> rotations)
    {
        cachedPositions = new List<Vector3>(positions);
        cachedRotations = new List<Quaternion>(rotations);
    }

    void UpdateLastPointGhost()
    {
        if (!EnsureGhost(ref lastPointGhost, "GhostGripper_Last"))
        {
            return;
        }

        int lastIndex = cachedPositions.Count - 1;
        if (lastIndex < 0)
        {
            return;
        }

        SetGhostPose(lastPointGhost, lastIndex);
    }

    void UpdateSelectedGhostPose()
    {
        if (!EnsureGhost(ref selectedPointGhost, "GhostGripper_Selected"))
        {
            return;
        }

        if (!HasCachedIndex(selectedGhostIndex))
        {
            return;
        }

        SetGhostPose(selectedPointGhost, selectedGhostIndex);
    }

    void SetGhostPose(GameObject ghost, int index)
    {
        ghost.transform.localPosition = cachedPositions[index];
        ghost.transform.localRotation = cachedRotations[index] * Quaternion.Euler(ghostRotationOffsetEuler);
    }

    bool HasCachedIndex(int index)
    {
        return index >= 0 && index < cachedPositions.Count && index < cachedRotations.Count;
    }

    bool EnsureGhost(ref GameObject ghost, string name)
    {
        if (ghost != null)
        {
            return true;
        }

        GameObject prefab = GetGhostGripperPrefab();
        if (prefab == null)
        {
            return false;
        }

        ghost = Instantiate(prefab, trajectoryContainer.transform);
        ghost.name = name;
        ghost.transform.localScale = Vector3.one * ghostScale;
        ApplyGhostAppearance(ghost);
        return true;
    }

    void UpdateGhostGripperJawWidth()
    {
        float width = GetLiveGripperWidth();
        UpdateJawWidthForGhost(lastPointGhost, width);
        UpdateJawWidthForGhost(selectedPointGhost, width);
    }

    void UpdateJawWidthForGhost(GameObject ghost, float width)
    {
        if (ghost == null)
        {
            return;
        }

        GripperJawController jawController = ghost.GetComponentInChildren<GripperJawController>();
        if (jawController != null)
        {
            jawController.jawWidth = width;
        }
    }

    float GetLiveGripperWidth()
    {
        if (VisualizationServer.instance != null && VisualizationServer.instance.leftGripperWidth > 0f)
        {
            return VisualizationServer.instance.leftGripperWidth;
        }

        return ghostJawWidth;
    }

    GameObject GetGhostGripperPrefab()
    {
        if (ghostGripperPrefab != null)
        {
            return ghostGripperPrefab;
        }

        ghostGripperPrefab = Resources.Load<GameObject>(ghostGripperResourcePath);
        if (ghostGripperPrefab == null)
        {
            Debug.LogWarning($"ChunkVisualizer: GhostGripper prefab not found at Resources/{ghostGripperResourcePath}");
        }
        return ghostGripperPrefab;
    }

    void ApplyGhostAppearance(GameObject ghost)
    {
        foreach (Collider collider in ghost.GetComponentsInChildren<Collider>())
        {
            Destroy(collider);
        }

        foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>())
        {
            if (renderer.sharedMaterial == null)
            {
                continue;
            }

            Material material = new Material(renderer.sharedMaterial);
            Color color = material.color;
            color.a = ghostAlpha;
            material.color = color;
            SetMaterialTransparent(material);
            renderer.material = material;
        }
    }

    void SetMaterialTransparent(Material material)
    {
        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
    }

    public void SetActionChunkVisible(bool isVisible)
    {
        showActionChunk = isVisible;

        foreach (GameObject point in pointObjects)
        {
            Renderer renderer = point.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = isVisible;
            }
        }
        foreach (GameObject line in lineObjects)
        {
            line.SetActive(isVisible);
        }
        foreach (GameObject axis in axisObjects)
        {
            axis.SetActive(isVisible);
        }
    }

    public void SetGhostGrippersVisible(bool isVisible)
    {
        showGhostGrippers = isVisible;
        UpdateGhostVisibility();
    }

    public void SetSelectedGhost(int pointIndex, bool isEditing)
    {
        selectedGhostIndex = pointIndex;
        selectedGhostEditing = isEditing;
        UpdateSelectedGhostPose();
        UpdateGhostVisibility();
    }

    void UpdateGhostVisibility()
    {
        int lastIndex = cachedPositions.Count - 1;
        bool hasLast = lastIndex >= 0;

        if (lastPointGhost != null)
        {
            lastPointGhost.SetActive(showGhostGrippers && hasLast);
        }

        bool showSelected = showGhostGrippers && selectedGhostEditing && HasCachedIndex(selectedGhostIndex);
        if (showSelected && selectedGhostIndex == lastIndex)
        {
            showSelected = false;
        }

        if (selectedPointGhost != null)
        {
            selectedPointGhost.SetActive(showSelected);
        }
    }
    
    // 通过索引设置点的悬停状态（用于VRController）
    public void SetPointHovered(int pointIndex, bool isHovered)
    {
        if (pointIndex < 0 || pointIndex >= pointObjects.Count) return;
        
        // 更新状态记录
        if (isHovered)
        {
            currentHoveredPointIndex = pointIndex;
        }
        else if (currentHoveredPointIndex == pointIndex)
        {
            currentHoveredPointIndex = -1;
        }
        
        TrajectoryPointData pointData = pointObjects[pointIndex].GetComponent<TrajectoryPointData>();
        if (pointData != null)
        {
            pointData.SetHovered(isHovered);
        }
    }
    
    // 通过索引设置点的选中状态（用于VRController）
    public void SetPointSelected(int pointIndex, bool isSelected)
    {
        if (pointIndex < 0 || pointIndex >= pointObjects.Count) return;
        
        // 更新状态记录
        if (isSelected)
        {
            currentSelectedPointIndex = pointIndex;
        }
        else if (currentSelectedPointIndex == pointIndex)
        {
            currentSelectedPointIndex = -1;
        }
        
        TrajectoryPointData pointData = pointObjects[pointIndex].GetComponent<TrajectoryPointData>();
        if (pointData != null)
        {
            pointData.SetSelected(isSelected);
        }
    }
    
    // 恢复点的选中和悬停状态（轨迹更新后调用）
    void RestorePointStates()
    {
        // 恢复选中状态
        if (currentSelectedPointIndex >= 0 && currentSelectedPointIndex < pointObjects.Count)
        {
            TrajectoryPointData pointData = pointObjects[currentSelectedPointIndex].GetComponent<TrajectoryPointData>();
            if (pointData != null)
            {
                pointData.SetSelected(true);
            }
        }
        
        // 恢复悬停状态（只在未选中时生效）
        if (currentHoveredPointIndex >= 0 && 
            currentHoveredPointIndex < pointObjects.Count &&
            currentHoveredPointIndex != currentSelectedPointIndex)
        {
            TrajectoryPointData pointData = pointObjects[currentHoveredPointIndex].GetComponent<TrajectoryPointData>();
            if (pointData != null)
            {
                pointData.SetHovered(true);
            }
        }
    }
    
    // 清除所有选中/悬停状态
    public void ClearAllStates()
    {
        currentSelectedPointIndex = -1;
        currentHoveredPointIndex = -1;
    }

    public int GetLastPointIndex()
    {
        return pointObjects.Count > 0 ? pointObjects.Count - 1 : -1;
    }
    
    void OnDestroy()
    {
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Abort();
        }
        if (server != null)
        {
            server.Close();
        }
        
        if (trajectoryContainer != null)
        {
            Destroy(trajectoryContainer);
        }
    }
}
