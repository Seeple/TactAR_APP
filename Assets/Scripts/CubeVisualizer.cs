using UnityEngine;
using System.Net.Sockets;
using System.Net;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using System.IO;
using System;
using System.Threading;
using System.Collections.Generic;

/*
1. smoothFollow = true: 平滑跟随用户的位置和旋转
2. smoothFollow = false: 接收工作站坐标进行遥操作移动
*/

public class CubeVisualizer : MonoBehaviour
{
    [Header("立方体设置")]
    public GameObject cubePrefab; 
    public float distanceFromUser = 2.0f;
    public float cubeSize = 0.5f; 
    public Color cubeColor = Color.blue; 

    [Header("跟随设置")]
    public bool smoothFollow = true;
    public float followSpeed = 5.0f; 

    [Header("位置更新")]
    public float updateFrequency = 50.0f; 

    [Header("参考对象")]
    public Transform userHead; 
    public Transform leftController; 
    
    [Header("遥操作设置")]
    public int remoteControlPort = 10007;
    
    private GameObject cubeInstance; 
    private Renderer cubeRenderer;
    private float lastUpdateTime = 0f; 
    private Vector3 fixedPosition; 
    private Quaternion fixedRotation; 
    private bool isFixedPositionSet = false;
    
    // Variables for remote control
    private Vector3 initialLeftControllerPosition;
    private Quaternion initialLeftControllerRotation;
    private Vector3 remoteControllerPosition;
    private Quaternion remoteControllerRotation;
    private bool hasRemoteData = false;
    private UdpClient remoteControlServer;
    private Thread remoteControlThread;
    private JsonSerializer serializer = new JsonSerializer();
    
    [DataContract]
    public class RemoteControlMessage
    {
        [DataMember]
        public List<float> position { get; set; } // [x, y, z]
        [DataMember]
        public List<float> rotation { get; set; } // [x, y, z, w] - quaternion
    } 

    void Start()
    {
        if (userHead == null)
        {
            Debug.LogError("CubeVisualizer: userHead未设置,请在Unity Editor中指定用户头部Transform");
            return;
        }

        if (leftController == null)
        {
            Debug.LogError("CubeVisualizer: leftController未设置,请在Unity Editor中指定左手控制器Transform");
            return;
        }

        CreateCube();
        
        if (!smoothFollow && userHead != null)
        {
            SetFixedPosition();
            RecordInitialLeftControllerTransform();
        }
        
        StartRemoteControlServer();
    }

    void Update()
    {
        if (cubeInstance != null && userHead != null)
        {
            if (Time.time - lastUpdateTime >= 1f / updateFrequency)
            {
                UpdateCubePosition();
                lastUpdateTime = Time.time;
            }
        }
    }
    void CreateCube()
    {
        if (cubePrefab != null)
        {
            cubeInstance = Instantiate(cubePrefab);
        }
        else
        {
            Debug.LogWarning("CubeVisualizer: 未设置cubePrefab");
        }

        cubeInstance.transform.SetParent(this.transform);

        if (userHead != null)
        {
            UpdateCubePosition();
        }
    }

    void UpdateCubePosition()
    {
        if (smoothFollow)
        {
            // smooth follow mode: follow user position and rotation
            Vector3 targetPosition = userHead.position + userHead.forward * distanceFromUser;
            Quaternion targetRotation = userHead.rotation;

            cubeInstance.transform.position = Vector3.Lerp(
                cubeInstance.transform.position,
                targetPosition,
                followSpeed * Time.deltaTime
            );

            cubeInstance.transform.rotation = Quaternion.Lerp(
                cubeInstance.transform.rotation,
                targetRotation,
                followSpeed * Time.deltaTime
            );
        }
        else
        {
            // remote control mode: use remote controller data to move cube
            if (!isFixedPositionSet)
            {
                SetFixedPosition();
                RecordInitialLeftControllerTransform();
            }
            
            if (hasRemoteData && leftController != null)
            {
                Vector3 controllerPositionDelta = remoteControllerPosition - initialLeftControllerPosition;
                Quaternion controllerRotationDelta = remoteControllerRotation * Quaternion.Inverse(initialLeftControllerRotation);
                
                cubeInstance.transform.position = fixedPosition + controllerPositionDelta;
                cubeInstance.transform.rotation = controllerRotationDelta * fixedRotation;
            }
            else
            {
                cubeInstance.transform.position = fixedPosition;
                cubeInstance.transform.rotation = fixedRotation;
            }
        }
    }

    public void SetFixedPosition()
    {
        if (userHead != null)
        {
            fixedPosition = userHead.position + userHead.forward * distanceFromUser;
            fixedRotation = userHead.rotation;
            isFixedPositionSet = true;
            Debug.Log($"CubeVisualizer: 设置固定位置为 {fixedPosition}");
        }
    }
    
    // change mode:smooth follow or remote control
    public void SetSmoothFollow(bool smooth)
    {
        smoothFollow = smooth;
        if (!smooth)
        {
            SetFixedPosition();
            RecordInitialLeftControllerTransform();
        }
        Debug.Log($"CubeVisualizer: 跟随模式设置为 {(smooth ? "平滑跟随" : "遥操作模式")}");
    }
    
    // record left controller initial transform
    void RecordInitialLeftControllerTransform()
    {
        if (leftController != null)
        {
            initialLeftControllerPosition = leftController.position;
            initialLeftControllerRotation = leftController.rotation;
            Debug.Log($"CubeVisualizer: 记录初始左手控制器位置 {initialLeftControllerPosition}");
        }
    }

    void StartRemoteControlServer()
    {
        remoteControlThread = new Thread(RemoteControlServer);
        remoteControlThread.Start();
        Debug.Log($"CubeVisualizer: 启动遥控服务器，监听端口 {remoteControlPort}");
    }
    
    void RemoteControlServer()
    {
        remoteControlServer = new UdpClient(remoteControlPort);
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        
        while (true)
        {
            try
            {
                byte[] receiveBytes = remoteControlServer.Receive(ref remoteEndPoint);
                using (MemoryStream ms = new MemoryStream(receiveBytes))
                {
                    using (BsonReader reader = new BsonReader(ms))
                    {
                        RemoteControlMessage remoteData = serializer.Deserialize<RemoteControlMessage>(reader);
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => {
                            UpdateRemoteControlData(remoteData);
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"CubeVisualizer遥控数据接收错误: {e.Message}");
            }
        }
    }
    
    void UpdateRemoteControlData(RemoteControlMessage data)
    {
        if (data.position != null && data.position.Count >= 3 &&
            data.rotation != null && data.rotation.Count >= 4)
        {
            remoteControllerPosition = new Vector3(data.position[0], data.position[1], data.position[2]);
            remoteControllerRotation = new Quaternion(data.rotation[0], data.rotation[1], data.rotation[2], data.rotation[3]);
            hasRemoteData = true;
        }
    }

    void OnDestroy()
    {
        if (remoteControlThread != null && remoteControlThread.IsAlive)
        {
            remoteControlThread.Abort();
        }
        if (remoteControlServer != null)
        {
            remoteControlServer.Close();
        }
        if (cubeInstance != null)
        {
            DestroyImmediate(cubeInstance);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (userHead != null)
        {
            Vector3 cubePosition;
            if (!smoothFollow && isFixedPositionSet)
            {
                cubePosition = fixedPosition;
                Gizmos.color = Color.red; // red: fixed position
            }
            else
            {
                cubePosition = userHead.position + userHead.forward * distanceFromUser;
                Gizmos.color = cubeColor; // blue: smooth follow
            }
            
            Gizmos.DrawWireCube(cubePosition, Vector3.one * cubeSize);

            // line from user head to cube for debug
            Gizmos.color = smoothFollow ? Color.yellow : new Color(1f, 0.5f, 0f); 
            Gizmos.DrawLine(userHead.position, cubePosition);
            
            // Draw user head position
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(userHead.position, 0.1f);
            
            // Draw user head forward direction
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(userHead.position, userHead.forward * distanceFromUser);
        }
    }
}
