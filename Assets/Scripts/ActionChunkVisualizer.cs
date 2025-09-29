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
    
    private Vector3 referencePosition = Vector3.zero;
    private Quaternion referenceRotation = Quaternion.identity;
    private bool isReferenceSet = false;
    
    [Header("基准点设置")]
    public bool isInReferenceMode = false;
    public float referenceIndicatorSize = 0.05f;
    public GameObject referenceIndicatorPrefab; 
    private GameObject referenceIndicator;
    
    private UdpClient server;
    private Thread receiveThread;
    private JsonSerializer serializer = new JsonSerializer();
    
    private TrajectoryData currentTrajectoryData;
    private bool hasNewData = false;
    
    private GameObject trajectoryContainer;
    private List<GameObject> pointObjects = new List<GameObject>();
    private List<GameObject> lineObjects = new List<GameObject>();
    private List<GameObject> axisObjects = new List<GameObject>();
    
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
        StartReceivingData();
        
        if (referenceIndicatorPrefab != null)
        {
            referenceIndicator = Instantiate(referenceIndicatorPrefab);
            referenceIndicator.name = "ReferenceIndicator";
            referenceIndicator.transform.SetParent(this.transform);
            referenceIndicator.transform.localScale = Vector3.one * referenceIndicatorSize;
            referenceIndicator.SetActive(false); 
            Debug.Log("ChunkVisualizer: 预创建基准点指示器Prefab");
        }
    }
    
    void Update()
    {
        if (hasNewData && isReferenceSet)
        {
            hasNewData = false;
            UpdateTrajectoryVisualization();
        }
    }
    
    public void ToggleReferenceMode()
    {
        isInReferenceMode = !isInReferenceMode;
        
        if (isInReferenceMode)
        {
            Debug.Log("ChunkVisualizer: 进入基准点设置模式");
            CreateReferenceIndicator();
        }
        else
        {
            Debug.Log("ChunkVisualizer: 退出基准点设置模式");
            DestroyReferenceIndicator();
        }
    }
     
    
    void CreateReferenceIndicator()
    {
        if (referenceIndicatorPrefab != null && referenceIndicator != null)
        {
            referenceIndicator.SetActive(true);
            Debug.Log("ChunkVisualizer: 激活预创建的基准点指示器");
            return;
        }
        
        if (referenceIndicatorPrefab == null)
        {
            Debug.LogError("ChunkVisualizer: 请在Inspector中设置Reference Indicator Prefab");
            return;
        }
        
        referenceIndicator = Instantiate(referenceIndicatorPrefab);
        referenceIndicator.name = "ReferenceIndicator";
        referenceIndicator.transform.SetParent(this.transform);
        referenceIndicator.transform.localScale = Vector3.one * referenceIndicatorSize;
        referenceIndicator.SetActive(true);
        
        Debug.Log($"ChunkVisualizer: 基准点指示器已创建，位置: {referenceIndicator.transform.position}");
    }
    
    void DestroyReferenceIndicator()
    {
        if (referenceIndicator != null)
        {
            referenceIndicator.SetActive(false);
            Debug.Log("ChunkVisualizer: 基准点指示器已隐藏");
        }
    }

    private float lastIndicatorUpdateTime = 0f;
    public float indicatorUpdateInterval = 0.1f;

    public void UpdateReferenceIndicator(Vector3 position, Quaternion rotation)
    {
        if (isInReferenceMode && referenceIndicator != null)
        {
            if (Time.time - lastIndicatorUpdateTime >= indicatorUpdateInterval)
            {
                referenceIndicator.transform.position = position;
                referenceIndicator.transform.rotation = rotation;
                lastIndicatorUpdateTime = Time.time;
                
                if (!referenceIndicator.activeInHierarchy)
                {
                    Debug.LogWarning("ChunkVisualizer: 基准点指示器应该可见但处于非激活状态");
                    referenceIndicator.SetActive(true);
                }
            }
        }
    }
    
    // public void DebugIndicatorStatus()
    // {
    //     Debug.Log($"ChunkVisualizer Debug - isInReferenceMode: {isInReferenceMode}");
    //     Debug.Log($"ChunkVisualizer Debug - referenceIndicatorPrefab null: {referenceIndicatorPrefab == null}");
    //     Debug.Log($"ChunkVisualizer Debug - referenceIndicator null: {referenceIndicator == null}");
    //     if (referenceIndicator != null)
    //     {
    //         Debug.Log($"ChunkVisualizer Debug - referenceIndicator active: {referenceIndicator.activeInHierarchy}");
    //         Debug.Log($"ChunkVisualizer Debug - referenceIndicator position: {referenceIndicator.transform.position}");
    //         Debug.Log($"ChunkVisualizer Debug - referenceIndicator scale: {referenceIndicator.transform.localScale}");
            
    //         Renderer renderer = referenceIndicator.GetComponent<Renderer>();
    //         if (renderer != null)
    //         {
    //             Debug.Log($"ChunkVisualizer Debug - renderer enabled: {renderer.enabled}");
    //             Debug.Log($"ChunkVisualizer Debug - material: {renderer.material?.name}");
    //         }
    //     }
    // }
    
    // set the reference position and rotation based on OVRhead
    public void SetReference(Vector3 position, Quaternion rotation)
    {
        referencePosition = position;
        referenceRotation = rotation;
        isReferenceSet = true;
        Debug.Log($"ChunkVisualizer: 基准点已设置 - 位置: {position}, 旋转: {rotation.eulerAngles}");
        
        if (isInReferenceMode)
        {
            isInReferenceMode = false;
            Debug.Log("ChunkVisualizer: 退出基准点设置模式");
            DestroyReferenceIndicator();
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
                        TrajectoryData trajectoryData = serializer.Deserialize<TrajectoryData>(reader);
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => {
                            currentTrajectoryData = trajectoryData;
                            hasNewData = true;
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"ChunkVisualizer接收数据错误: {e.Message}");
            }
        }
    }


    // Update trajectory visualization with new data given from Workstation
    // TODO: optimize performance by reusing objects instead of destroying and recreating them
    void UpdateTrajectoryVisualization()
    {
        if (currentTrajectoryData == null || currentTrajectoryData.points == null) return;
        
        ClearVisualization();
        
        trajectoryContainer = new GameObject("ChunkTrajectoryVisualization");
        trajectoryContainer.transform.SetParent(this.transform);
        
        List<Vector3> worldPositions = ConvertPositionsToWorldCoordinates(currentTrajectoryData.points);
        List<Quaternion> worldRotations = ConvertRotationsToWorldCoordinates(currentTrajectoryData.points);

        CreateTrajectoryPoints(worldPositions);
        CreateConnectionLines(worldPositions);
        CreateCoordinateAxes(worldPositions, worldRotations);
    }
    
    // Convert relative trajectory points to world coordinates based on reference position and rotation
    List<Vector3> ConvertPositionsToWorldCoordinates(List<TrajectoryPoint> relativePoints)
    {
        List<Vector3> worldPositions = new List<Vector3>();
        
        for (int i = 0; i < relativePoints.Count; i++)
        {
            TrajectoryPoint relativePoint = relativePoints[i];
            Vector3 relativePosition = new Vector3(relativePoint.x, relativePoint.y, relativePoint.z);
            
            Vector3 worldPosition = referencePosition + referenceRotation * relativePosition;
            worldPositions.Add(worldPosition);
        }
        
        return worldPositions;
    }
    
    // Convert relative rotations to world rotations
    List<Quaternion> ConvertRotationsToWorldCoordinates(List<TrajectoryPoint> relativePoints)
    {
        List<Quaternion> worldRotations = new List<Quaternion>();
        
        for (int i = 0; i < relativePoints.Count; i++)
        {
            TrajectoryPoint relativePoint = relativePoints[i];
            Quaternion relativeRotation = Quaternion.Euler(relativePoint.pitch, relativePoint.yaw, relativePoint.roll);
            
            Quaternion worldRotation = referenceRotation * relativeRotation;
            worldRotations.Add(worldRotation);
        }
        
        return worldRotations;
    }

    // Create trajectory points as spheres
    void CreateTrajectoryPoints(List<Vector3> worldPositions)
    {
        for (int i = 0; i < worldPositions.Count; i++)
        {
            GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = $"ChunkPoint_{i}";
            point.transform.SetParent(trajectoryContainer.transform);
            point.transform.position = worldPositions[i];
            point.transform.localScale = Vector3.one * pointSize;
            
            Renderer renderer = point.GetComponent<Renderer>();
            renderer.material = whiteMaterial;
            
            DestroyImmediate(point.GetComponent<Collider>());
            pointObjects.Add(point);
        }
    }
    
    // Create lines connecting trajectory points
    void CreateConnectionLines(List<Vector3> worldPositions)
    {
        for (int i = 0; i < worldPositions.Count - 1; i++)
        {
            GameObject line = CreateLine(worldPositions[i], worldPositions[i + 1], $"ChunkLine_{i}_{i + 1}", 0.5f, 1.0f);
            lineObjects.Add(line);
        }
    }
    
    // Create coordinate axes at each trajectory point
    void CreateCoordinateAxes(List<Vector3> worldPositions, List<Quaternion> worldRotations)
    {
        for (int i = 0; i < worldPositions.Count; i++)
        {
            Vector3 position = worldPositions[i];
            Quaternion rotation = worldRotations[i];
            
            // calculate local axis directions
            Vector3 rightDirection = rotation * Vector3.right;
            Vector3 upDirection = rotation * Vector3.up;
            Vector3 forwardDirection = rotation * Vector3.forward;
            
            // x-axis (red)
            Vector3 xEnd = position + rightDirection * axisLength;
            GameObject xAxis = CreateLine(position, xEnd, $"ChunkXAxis_{i}", 0.3f, 0.5f);
            xAxis.GetComponent<Renderer>().material = redMaterial;
            axisObjects.Add(xAxis);
            
            // y-axis (green）
            Vector3 yEnd = position + upDirection * axisLength;
            GameObject yAxis = CreateLine(position, yEnd, $"ChunkYAxis_{i}", 0.3f, 0.5f);
            yAxis.GetComponent<Renderer>().material = greenMaterial;
            axisObjects.Add(yAxis);
            
            // z-axis (blue)
            Vector3 zEnd = position + forwardDirection * axisLength;
            GameObject zAxis = CreateLine(position, zEnd, $"ChunkZAxis_{i}", 0.3f, 0.5f);
            zAxis.GetComponent<Renderer>().material = blueMaterial;
            axisObjects.Add(zAxis);
        }
    }
    
    // create lines as cylinders with customizable thickness and length scale
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
        
        line.transform.position = center;
        line.transform.rotation = Quaternion.LookRotation(direction);
        line.transform.Rotate(90, 0, 0);
        
        // 应用粗细和长度系数
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
            DestroyImmediate(trajectoryContainer);
        }
        
        pointObjects.Clear();
        lineObjects.Clear();
        axisObjects.Clear();
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
        DestroyReferenceIndicator();
        ClearVisualization();
    }
}
