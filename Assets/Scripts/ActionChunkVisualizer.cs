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
    
    private UdpClient server;
    private Thread receiveThread;
    private JsonSerializer serializer = new JsonSerializer();
    
    private GameObject trajectoryContainer;
    
    // Object pools for performance optimization
    private Queue<GameObject> pointPool = new Queue<GameObject>();
    private Queue<GameObject> linePool = new Queue<GameObject>();
    private Queue<GameObject> axisPool = new Queue<GameObject>();
    
    private List<GameObject> activePoints = new List<GameObject>();
    private List<GameObject> activeLines = new List<GameObject>();
    private List<GameObject> activeAxes = new List<GameObject>();

    [Header("性能设置")]
    public int poolInitialSize = 100;
    
    [Header("交互设置")]
    public bool enablePointSelection = true;  // 是否启用点选择
    public Color selectedPointColor = Color.yellow;  // 选中点的颜色
    public Color hoverPointColor = Color.cyan;  // 悬停点的颜色
    
    // 记录选中和悬停状态（用于轨迹更新后恢复）
    private int currentSelectedPointIndex = -1;
    private int currentHoveredPointIndex = -1;
    
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
        InitializeObjectPools(); 
        StartReceivingData();
    }
    
    void InitializeObjectPools()
    {
        trajectoryContainer = new GameObject("ChunkTrajectoryVisualization");
        
        // 将容器设为 Calibration 的子对象，使可视化自动受校准影响
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
        
        for (int i = 0; i < poolInitialSize; i++)
        {
            GameObject point = CreatePooledPoint();
            point.SetActive(false);
            pointPool.Enqueue(point);
            
            GameObject line = CreatePooledLine();
            line.SetActive(false);
            linePool.Enqueue(line);
            
            for (int j = 0; j < 3; j++)
            {
                GameObject axis = CreatePooledLine();
                axis.SetActive(false);
                axisPool.Enqueue(axis);
            }
        }
        
        Debug.Log($"ChunkVisualizer: Object Pool successfully initialized {poolInitialSize} points, lines, and axes.");
    }
    
    GameObject CreatePooledPoint()
    {
        GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        point.name = "PooledChunkPoint";
        point.transform.SetParent(trajectoryContainer.transform);
        point.transform.localScale = Vector3.one * pointSize;
        
        Renderer renderer = point.GetComponent<Renderer>();
        renderer.material = whiteMaterial;
        
        // 如果启用点选择，保留Collider并添加TrajectoryPointData组件
        if (enablePointSelection)
        {
            // 保留SphereCollider用于射线检测
            SphereCollider collider = point.GetComponent<SphereCollider>();
            if (collider != null)
            {
                collider.isTrigger = false;  // 确保可以被射线检测到
            }
            
            // 添加TrajectoryPointData组件
            TrajectoryPointData pointData = point.AddComponent<TrajectoryPointData>();
            pointData.visualizer = this;
        }
        else
        {
            DestroyImmediate(point.GetComponent<Collider>());
        }
        
        return point;
    }
    
    GameObject CreatePooledLine()
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        line.name = "PooledChunkLine";
        line.transform.SetParent(trajectoryContainer.transform);
        
        Renderer renderer = line.GetComponent<Renderer>();
        renderer.material = whiteMaterial;
        DestroyImmediate(line.GetComponent<Collider>());
        
        return line;
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
                        TrajectoryData trajectoryData = serializer.Deserialize<TrajectoryData>(reader);
                        
                        // Visualzation in main thread without latency
                        UnityMainThreadDispatcher.Instance().Enqueue(() => {
                            UpdateTrajectoryVisualization(trajectoryData);
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Action Chunk Visualizer Failed to Receive messages: {e.Message}");
            }
        }
    }


    void UpdateTrajectoryVisualization(TrajectoryData trajectoryData)
    {
        if (trajectoryData == null || trajectoryData.points == null) return;
        
        ReturnObjectsToPool(); // return active objects to pool
        
        List<Vector3> positions = ExtractPositions(trajectoryData.points);
        List<Quaternion> rotations = ExtractRotations(trajectoryData.points);

        CreateTrajectoryPoints(positions);
        CreateConnectionLines(positions);
        CreateCoordinateAxes(positions, rotations);
        
        // 轨迹更新后恢复选中和悬停状态
        RestorePointStates();
    }
    
    List<Vector3> ExtractPositions(List<TrajectoryPoint> points)
    {
        List<Vector3> positions = new List<Vector3>();
        
        for (int i = 0; i < points.Count; i++)
        {
            TrajectoryPoint point = points[i];
            Vector3 position = new Vector3(point.x, point.y, point.z);
            positions.Add(position);
        }
        
        return positions;
    }
    
    List<Quaternion> ExtractRotations(List<TrajectoryPoint> points)
    {
        List<Quaternion> rotations = new List<Quaternion>();
        
        for (int i = 0; i < points.Count; i++)
        {
            TrajectoryPoint point = points[i];
            Quaternion rotation = Quaternion.Euler(point.pitch, point.yaw, point.roll);
            rotations.Add(rotation);
        }
        
        return rotations;
    }

    // Create trajectory points using object pool
    void CreateTrajectoryPoints(List<Vector3> positions)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            GameObject point = GetPointFromPool();
            // 使用 localPosition 而非 position，使其相对于父对象（Calibration）
            point.transform.localPosition = positions[i];
            
            // 如果启用点选择，设置点的索引
            if (enablePointSelection)
            {
                TrajectoryPointData pointData = point.GetComponent<TrajectoryPointData>();
                if (pointData != null)
                {
                    pointData.pointIndex = i;
                    pointData.visualizer = this;
                }
            }
            
            point.SetActive(true);
            activePoints.Add(point);
        }
    }
    
    // Create lines connecting trajectory points using object pool
    void CreateConnectionLines(List<Vector3> positions)
    {
        for (int i = 0; i < positions.Count - 1; i++)
        {
            GameObject line = GetLineFromPool();
            SetupLine(line, positions[i], positions[i + 1], whiteMaterial, 0.5f, 1.0f);
            line.SetActive(true);
            activeLines.Add(line);
        }
    }
    
    // Create coordinate axes at each trajectory point using object pool
    void CreateCoordinateAxes(List<Vector3> positions, List<Quaternion> rotations)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 position = positions[i];
            Quaternion rotation = rotations[i];
            
            // calculate local axis directions
            Vector3 rightDirection = rotation * Vector3.right;
            Vector3 upDirection = rotation * Vector3.up;
            Vector3 forwardDirection = rotation * Vector3.forward;
            
            // x-axis (red)
            Vector3 xEnd = position + rightDirection * axisLength;
            GameObject xAxis = GetAxisFromPool();
            SetupLine(xAxis, position, xEnd, redMaterial, 0.3f, 0.5f);
            xAxis.SetActive(true);
            activeAxes.Add(xAxis);
            
            // y-axis (green)
            Vector3 yEnd = position + upDirection * axisLength;
            GameObject yAxis = GetAxisFromPool();
            SetupLine(yAxis, position, yEnd, greenMaterial, 0.3f, 0.5f);
            yAxis.SetActive(true);
            activeAxes.Add(yAxis);
            
            // z-axis (blue)
            Vector3 zEnd = position + forwardDirection * axisLength;
            GameObject zAxis = GetAxisFromPool();
            SetupLine(zAxis, position, zEnd, blueMaterial, 0.3f, 0.5f);
            zAxis.SetActive(true);
            activeAxes.Add(zAxis);
        }
    }
    
    GameObject GetPointFromPool()
    {
        if (pointPool.Count > 0)
        {
            return pointPool.Dequeue();
        }
        else
        {
            return CreatePooledPoint();
        }
    }
    
    GameObject GetLineFromPool()
    {
        if (linePool.Count > 0)
        {
            return linePool.Dequeue();
        }
        else
        {
            return CreatePooledLine();
        }
    }
    
    GameObject GetAxisFromPool()
    {
        if (axisPool.Count > 0)
        {
            return axisPool.Dequeue();
        }
        else
        {
            return CreatePooledLine();
        }
    }
    
    void SetupLine(GameObject line, Vector3 start, Vector3 end, Material material, float thicknessFactor = 1.0f, float lengthFactor = 1.0f)
    {
        Vector3 direction = end - start;
        float originalLength = direction.magnitude;
    
        float scaledLength = originalLength * lengthFactor;
        Vector3 scaledEnd = start + direction.normalized * scaledLength;
        Vector3 center = (start + scaledEnd) / 2f;
        
        // 使用 localPosition 和 localRotation，使线段相对于父对象（Calibration）
        line.transform.localPosition = center;
        line.transform.localRotation = Quaternion.LookRotation(direction);
        line.transform.Rotate(90, 0, 0);
        
        float thickness = lineWidth * thicknessFactor;
        line.transform.localScale = new Vector3(thickness * 2f, scaledLength / 2f, thickness * 2f);
        
        Renderer renderer = line.GetComponent<Renderer>();
        renderer.material = material;
    }
    
    void ReturnObjectsToPool()
    {
        foreach (GameObject point in activePoints)
        {
            point.SetActive(false);
            pointPool.Enqueue(point);
        }
        activePoints.Clear();
        
        foreach (GameObject line in activeLines)
        {
            line.SetActive(false);
            linePool.Enqueue(line);
        }
        activeLines.Clear();
        
        foreach (GameObject axis in activeAxes)
        {
            axis.SetActive(false);
            axisPool.Enqueue(axis);
        }
        activeAxes.Clear();
    }
    
    void ClearVisualization()
    {
        ReturnObjectsToPool();
    }
    
    // 通过索引设置点的悬停状态（用于VRController）
    public void SetPointHovered(int pointIndex, bool isHovered)
    {
        if (pointIndex < 0 || pointIndex >= activePoints.Count) return;
        
        // 更新状态记录
        if (isHovered)
        {
            currentHoveredPointIndex = pointIndex;
        }
        else if (currentHoveredPointIndex == pointIndex)
        {
            currentHoveredPointIndex = -1;
        }
        
        TrajectoryPointData pointData = activePoints[pointIndex].GetComponent<TrajectoryPointData>();
        if (pointData != null)
        {
            pointData.SetHovered(isHovered);
        }
    }
    
    // 通过索引设置点的选中状态（用于VRController）
    public void SetPointSelected(int pointIndex, bool isSelected)
    {
        if (pointIndex < 0 || pointIndex >= activePoints.Count) return;
        
        // 更新状态记录
        if (isSelected)
        {
            currentSelectedPointIndex = pointIndex;
        }
        else if (currentSelectedPointIndex == pointIndex)
        {
            currentSelectedPointIndex = -1;
        }
        
        TrajectoryPointData pointData = activePoints[pointIndex].GetComponent<TrajectoryPointData>();
        if (pointData != null)
        {
            pointData.SetSelected(isSelected);
        }
    }
    
    // 新增：恢复点的选中和悬停状态（轨迹更新后调用）
    void RestorePointStates()
    {
        // 恢复选中状态
        if (currentSelectedPointIndex >= 0 && currentSelectedPointIndex < activePoints.Count)
        {
            TrajectoryPointData pointData = activePoints[currentSelectedPointIndex].GetComponent<TrajectoryPointData>();
            if (pointData != null)
            {
                pointData.pointIndex = currentSelectedPointIndex;
                pointData.visualizer = this;
                pointData.SetSelected(true);
            }
        }
        
        // 恢复悬停状态（只在未选中时生效）
        if (currentHoveredPointIndex >= 0 && 
            currentHoveredPointIndex < activePoints.Count &&
            currentHoveredPointIndex != currentSelectedPointIndex)
        {
            TrajectoryPointData pointData = activePoints[currentHoveredPointIndex].GetComponent<TrajectoryPointData>();
            if (pointData != null)
            {
                pointData.pointIndex = currentHoveredPointIndex;
                pointData.visualizer = this;
                pointData.SetHovered(true);
            }
        }
    }
    
    // 清除所有选中/悬停状态（可选，用于重置）
    public void ClearAllStates()
    {
        currentSelectedPointIndex = -1;
        currentHoveredPointIndex = -1;
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
        ClearVisualization();
        
        if (trajectoryContainer != null)
        {
            DestroyImmediate(trajectoryContainer);
        }
    }
}
