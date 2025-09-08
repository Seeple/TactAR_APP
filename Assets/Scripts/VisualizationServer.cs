using System.Collections.Generic;
using System.Net;
using UnityEngine;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using System.IO;
using UnityEngine.UI;

/*
The component for receiving visualization data from the workstation via UDP 
and rendering them in AR.
The data includes:
Robot: TCP pose of the robot.
Arrow: 3D deformation field of tactile sensors.
Force: 3D deformation field of force sensors.
Image: Image streams of RGB/tactile cameras.
*/

public class VisualizationServer : MonoBehaviour
{
    public static VisualizationServer instance;
    public Transform head;
    public int portRobot = 10001;
    public int portArrow = 10002;
    public int portLog = 10003;
    public int portImage = 10004;
    public int portForce = 10005;
    public Transform leftTCP;
    public Transform rightTCP;
    public Transform leftForce;
    public Transform rightForce;
    UdpClient serverRobot;
    UdpClient serverArrow;
    UdpClient serverLog;
    UdpClient serverImage;
    UdpClient serverForce;

    JsonSerializer serializer = new JsonSerializer();

    Transform point;
    Thread threadRobot, threadArrow, threadLog, threadImage, threadForce;

    /*
    Initialization of the UDP servers and threads.
    */
    private void Awake()
    {
        instance = this;
    }

    /*
    Start the UDP servers and threads.
    Five different threads are used to receive five different types of data:
    Robot, Arrow, Log, Image, Force.
    */
    void Start()
    {
        point = new GameObject().transform;
        threadRobot = new Thread(ServerRobot);
        threadRobot.Start();
        threadArrow = new Thread(ServerArrow);
        threadArrow.Start();
        threadLog = new Thread(ServerLog);
        threadLog.Start();
        threadImage = new Thread(ServerImage);
        threadImage.Start();
        threadForce = new Thread(ServerForce);
        threadForce.Start();
    }


    void ServerRobot()
    {
        serverRobot = new UdpClient(portRobot);
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            try
            {
                byte[] receiveBytes = serverRobot.Receive(ref remoteEndPoint);
                using (MemoryStream ms = new MemoryStream(receiveBytes))
                {
                    using (BsonReader reader = new BsonReader(ms))
                    {
                        BimanualRobotStates pose = serializer.Deserialize<BimanualRobotStates>(reader);
                        // update the robot's position and rotation in the AR scene on the main thread.
                        UnityMainThreadDispatcher.Instance().Enqueue(() => UpdateRobot(pose));
                    }
                }
                //BimanualRobotStates pose = MessagePackSerializer.Deserialize<BimanualRobotStates>(receiveBytes);
                //UnityMainThreadDispatcher.Instance().Enqueue(() => UpdateRobot(pose));
            }
            catch (Exception e)
            {
                Debug.LogError($"SocketException: {e.Message}");
            }

        }
    }

    void ServerArrow()
    {
        serverArrow = new UdpClient(portArrow);
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            try
            {
                byte[] receiveBytes = serverArrow.Receive(ref remoteEndPoint);
                using (MemoryStream ms = new MemoryStream(receiveBytes))
                {
                    using (BsonReader reader = new BsonReader(ms))
                    {
                        TactileSensorMessage sensorData = serializer.Deserialize<TactileSensorMessage>(reader);
                        UnityMainThreadDispatcher.Instance().Enqueue(() => UpdateArrow(sensorData));
                    }
                }
                //TactileSensorMessage sensorData = MessagePackSerializer.Deserialize<TactileSensorMessage>(receiveBytes);
                //UnityMainThreadDispatcher.Instance().Enqueue(() => UpdateArrow(sensorData));
            }
            catch (Exception e)
            {
                Debug.LogError($"SocketException: {e.Message}");
            }
        }
    }

    void ServerLog()
    {
        serverLog = new UdpClient(portLog);
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            try
            {
                byte[] receiveBytes = serverLog.Receive(ref remoteEndPoint);
                using (MemoryStream ms = new MemoryStream(receiveBytes))
                {
                    using (BsonReader reader = new BsonReader(ms))
                    {
                        UIMessage sensorData = serializer.Deserialize<UIMessage>(reader);
                        // Currently not displaying the log messages in the AR scene.
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"SocketException: {e.Message}");
            }
        }
    }

    void ServerImage()
    {
        serverImage = new UdpClient(portImage);
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            try
            {
                ReceiveImage(serverImage, remoteEndPoint);
            }
            catch (Exception e)
            {
                //logText.text = e.Message + Guid.NewGuid().ToString();
                Debug.LogError($"SocketException: {e.Message}");
            }
        }
    }
    
    //List<byte> bytes = new List<byte>();
    void ReceiveImage(UdpClient serverImage, IPEndPoint remoteEndPoint)
    {
        // ServerImage.Receive(ref remoteEndPoint):
        // First 4 bytes: total length of the image data (uint)
        // Second 4 bytes: chunk size (uint) 
        byte[] lengthBytes = serverImage.Receive(ref remoteEndPoint);
        if (lengthBytes.Length != 4)
            throw new Exception();
        uint lengthInt = BitConverter.ToUInt32(lengthBytes, 0);

        byte[] chunkBytes = serverImage.Receive(ref remoteEndPoint);
        if (chunkBytes.Length != 4)
            throw new Exception();
        uint lengthChunk = BitConverter.ToUInt32(chunkBytes, 0);

        //bytes.Clear();
        byte[] bytes = new byte[lengthInt];
        int count = Mathf.CeilToInt(lengthInt / (float)lengthChunk);
        for (int index = 0; index < count; index++)
        {
            byte[] buffer = serverImage.Receive(ref remoteEndPoint);
            //bytes.AddRange(buffer);
            Buffer.BlockCopy(buffer, 0, bytes, index * (int)lengthChunk, buffer.Length);
        }
        using (MemoryStream ms = new MemoryStream(bytes))
        {
            using (BsonReader reader = new BsonReader(ms))
            {
                ImageMessage imageData = serializer.Deserialize<ImageMessage>(reader);
                UnityMainThreadDispatcher.Instance().Enqueue(() => UpdateImage(imageData));
            }
        }
    }
    void ServerForce()
    {
        serverForce = new UdpClient(portForce); // Create a UDP server that listens on port:{portForce}
                                                // Create an endpoint to store the sender's IP address and port number
                                                // IPAddress.Any: accept messages from "any" IP address
                                                // 0: accept messages from "any" port
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (true)
        {
            try
            {
                byte[] receiveBytes = serverForce.Receive(ref remoteEndPoint);
                using (MemoryStream ms = new MemoryStream(receiveBytes))
                {
                    using (BsonReader reader = new BsonReader(ms))
                    {
                        ForceSensorMessage sensorData = serializer.Deserialize<ForceSensorMessage>(reader);
                        UnityMainThreadDispatcher.Instance().Enqueue(() => UpdateForce(sensorData));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"SocketException: {e.Message}");
            }
        }
    }

    void UpdateForce(ForceSensorMessage pose)
    {
        Transform forceArrow;
        if (pose.device_id == "left")
            forceArrow = leftForce;
        else
            forceArrow = rightForce;

        forceArrow.gameObject.SetActive(true);
        Vector3 start = new Vector3(pose.arrow.start[0], pose.arrow.start[1], pose.arrow.start[2]);
        Vector3 end = new Vector3(pose.arrow.end[0], pose.arrow.end[1], pose.arrow.end[2]);
        forceArrow.localPosition = end;
        
        point.parent = forceArrow.parent;
        point.localPosition = start;
        forceArrow.LookAt(point); // direction from end to start

        float length = Vector3.Distance(end, start);
        forceArrow.GetChild(0).localScale = Vector3.one * pose.scale[0];
        forceArrow.GetChild(1).localScale = new Vector3(pose.scale[1], length, pose.scale[2]);
    }

    [DataContract] // This attribute indicates that the class can be serialized.
    public class Image
    {
        [DataMember] // This attribute indicates that the property can be serialized.
        public string id { get; set; } // get: this property can be read. set: this property can be assigned a value.
        [DataMember]
        public bool inHeadSpace { get; set; }
        [DataMember]
        public bool leftOrRight { get; set; }
        [DataMember]
        public List<float> position { get; set; }
        [DataMember]
        public List<float> rotation { get; set; }
        [DataMember]
        public List<float> scale { get; set; }
        [DataMember]
        public byte[] image { get; set; }
    }
    [DataContract]
    public class ImageMessage
    {
        [DataMember]
        public List<Image> images { get; set; }
    }
    [DataContract]
    public class UIMessage
    {
        [DataMember]
        public string text { get; set; }
    }
    [DataContract]
    public class Arrow
    {
        [DataMember]
        public List<float> start { get; set; }
        [DataMember]
        public List<float> end { get; set; }
    }
    [DataContract]
    public class TactileSensorMessage
    {
        [DataMember]
        public string device_id { get; set; }
        [DataMember]
        public List<Arrow> arrows { get; set; }
        [DataMember]
        public List<float> scale { get; set; }
    }

    [DataContract]
    public class ForceSensorMessage
    {
        [DataMember]
        public string device_id { get; set; }
        [DataMember]
        public Arrow arrow { get; set; }
        [DataMember]
        public List<float> scale { get; set; }
    }
    [DataContract]
    public class BimanualRobotStates
    {
        [DataMember]
        public List<float> leftRobotTCP { get; set; }
        [DataMember]
        public List<float> rightRobotTCP { get; set; }
        [DataMember]
        public List<float> leftGripperState { get; set; }
        [DataMember]
        public List<float> rightGripperState { get; set; }
    }

    void UpdateRobot(BimanualRobotStates pose)
    {
        leftTCP.localPosition = new Vector3(pose.leftRobotTCP[0], pose.leftRobotTCP[1], pose.leftRobotTCP[2]);
        rightTCP.localPosition = new Vector3(pose.rightRobotTCP[0], pose.rightRobotTCP[1], pose.rightRobotTCP[2]);
        leftTCP.localRotation = new Quaternion(pose.leftRobotTCP[4], pose.leftRobotTCP[5], pose.leftRobotTCP[6], pose.leftRobotTCP[3]);
        rightTCP.localRotation = new Quaternion(pose.rightRobotTCP[4], pose.rightRobotTCP[5], pose.rightRobotTCP[6], pose.rightRobotTCP[3]);
    }

    Dictionary<string, Transform[]> arrowObjects = new Dictionary<string, Transform[]>(); // key: device_id, value: array of arrow objects
    public Color arrow1Color1;
    public Color arrow1Color2;
    public Color arrow2Color1;
    public Color arrow2Color2;
    public Transform arrow1;
    public Transform arrow2;
    public Material mat;
    Dictionary<Color32, Material> lerpColor = new Dictionary<Color32, Material>(); // key: color, value: material with the color
    
    void UpdateArrow(TactileSensorMessage data)
    {
        string device_id = data.device_id;
        if (!arrowObjects.ContainsKey(device_id))
        {
            arrowObjects[device_id] = new Transform[data.arrows.Count];
            for (int i = 0; i < data.arrows.Count; i++)
            {
                //arrowObjects[device_id][i] = Instantiate(arrow1).transform;
                if (device_id.EndsWith("1"))
                    arrowObjects[device_id][i] = Instantiate(arrow1).transform;
                //arrowObjects[device_id][i].GetComponent<Renderer>().material.SetColor("_EmissionColor", arrow1Color2);
                else
                    arrowObjects[device_id][i] = Instantiate(arrow2).transform;
                //arrowObjects[device_id][i].GetComponent<Renderer>().material.SetColor("_EmissionColor", arrow2Color2);
                arrowObjects[device_id][i].parent = GameObject.Find(device_id).transform;
            }
        }
        for (int i = 0; i < data.arrows.Count; i++)
        {
            Vector3 start = new Vector3(data.arrows[i].start[0], data.arrows[i].start[1], data.arrows[i].start[2]);
            Vector3 end = new Vector3(data.arrows[i].end[0], data.arrows[i].end[1], data.arrows[i].end[2]);
            arrowObjects[device_id][i].localPosition = end;
            point.parent = arrowObjects[device_id][i].parent;
            point.localPosition = start;
            arrowObjects[device_id][i].LookAt(point);
            float length = Vector3.Distance(end, start);
            Color32 color;
            if (device_id.EndsWith("1"))
                color = Color32.Lerp(arrow1Color1, arrow1Color2, length * 5);
            else
                color = Color32.Lerp(arrow2Color1, arrow2Color2, length * 5);
            color.r &= 0xFC;
            color.g &= 0xFC;
            color.b &= 0xFC;
            if (!lerpColor.ContainsKey(color))
            {
                lerpColor[color] = new Material(mat);
                lerpColor[color].SetColor("_EmissionColor", color);
            }
            arrowObjects[device_id][i].GetChild(1).GetChild(0).GetComponent<Renderer>().material = lerpColor[color];
            arrowObjects[device_id][i].GetChild(0).localScale = Vector3.one * data.scale[0];
            arrowObjects[device_id][i].GetChild(1).localScale = new Vector3(data.scale[1], length, data.scale[2]);
        }
    }

    public void ClearImage()
    {
        foreach (var item in imageUI)
        {
            Destroy(item.Value.gameObject);
        }
        imageUI.Clear();
    }
    Dictionary<string, RawImage> imageUI = new();
    public GameObject oneUI;
    void UpdateImage(ImageMessage imageData)
    {
        foreach (var item in imageData.images)
        {
            if (!imageUI.ContainsKey(item.id))
            {
                RawImage one = GameObject.Instantiate(oneUI).GetComponentInChildren<RawImage>();
                if (item.inHeadSpace)
                {
                    one.canvas.transform.SetParent(head);
                    if (!item.leftOrRight)
                        one.canvas.gameObject.layer = 6;
                    else
                        one.canvas.gameObject.layer = 7;
                }
                else
                    one.canvas.transform.SetParent(Calibration.instance.transform);
                one.texture = new Texture2D(1, 1);
                imageUI.Add(item.id, one);
            }
            imageUI[item.id].canvas.transform.localPosition = new Vector3(item.position[0], item.position[1], item.position[2]);
            imageUI[item.id].canvas.transform.localEulerAngles = new Vector3(item.rotation[0], item.rotation[1], item.rotation[2]);
            imageUI[item.id].canvas.transform.localScale = new Vector3(item.scale[0], item.scale[1], item.scale[2]);
            (imageUI[item.id].texture as Texture2D).LoadImage(item.image, true);
        }
    }
    private void OnDestroy()
    {
        serverRobot?.Close();
        serverArrow?.Close();
        serverLog?.Close();
        serverImage?.Close();
        serverForce?.Close();
        threadRobot.Abort();
        threadArrow.Abort();
        threadLog.Abort();
        threadImage.Abort();
        threadForce.Abort();
    }
}
