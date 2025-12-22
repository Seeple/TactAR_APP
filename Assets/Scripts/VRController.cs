using System;
using UnityEngine;
using System.Threading.Tasks;
using System.Text;
using System.Net.Http;

[Serializable]
public class TrajectoryEditMessage
{
    public int selectedPointIndex = -1;  // -1表示没有选中任何点
    public bool isEditing = false;
    public float[] editedPointPos;
    public float[] editedPointQuat;
    
    public TrajectoryEditMessage()
    {
        editedPointPos = new float[3];
        editedPointQuat = new float[4];
    }
    
    public void TransformToAlignSpace()
    {
        if (Calibration.instance && isEditing)
        {
            Vector3 vector3 = Calibration.instance.GetPosition(new Vector3(editedPointPos[0], editedPointPos[1], editedPointPos[2]));
            editedPointPos[0] = vector3.x;
            editedPointPos[1] = vector3.y;
            editedPointPos[2] = vector3.z;
            Quaternion quaternion = Calibration.instance.GetRotation(new Quaternion(editedPointQuat[1], editedPointQuat[2], editedPointQuat[3], editedPointQuat[0]));
            editedPointQuat[0] = quaternion.w;
            editedPointQuat[1] = quaternion.x;
            editedPointQuat[2] = quaternion.y;
            editedPointQuat[3] = quaternion.z;
        }
    }
}

[Serializable]
public class HandMessage
{
    public float[] wristPos;
    public float[] wristQuat;
    public float triggerState;
    public bool[] buttonState;
    public HandMessage()
    {
        wristPos = new float[3]; //position of the hand
        wristQuat = new float[4]; //quaternion of the hand
        buttonState = new bool[5]; //buttonState of B(Y)/A(X)/Thumbstick/IndexTrigger/HandTrigger
    }

    public void TransformToAlignSpace()
    {
        if (Calibration.instance)
        {
            Vector3 vector3 = Calibration.instance.GetPosition(new Vector3(wristPos[0], wristPos[1], wristPos[2])); // worldPos
            wristPos[0] = vector3.x;
            wristPos[1] = vector3.y;
            wristPos[2] = vector3.z;
            Quaternion quaternion = Calibration.instance.GetRotation(new Quaternion(wristQuat[1], wristQuat[2], wristQuat[3], wristQuat[0]));
            wristQuat[0] = quaternion.w;
            wristQuat[1] = quaternion.x;
            wristQuat[2] = quaternion.y;
            wristQuat[3] = quaternion.z;
        }
    }
}

[Serializable]
public class Message
{
    public float timestamp;
    public HandMessage rightHand;
    public HandMessage leftHand;
    public float[] headPos;
    public float[] headQuat;
    public TrajectoryEditMessage trajectoryEdit;  // 新增轨迹编辑信息
    
    public Message()
    {
        timestamp = Time.time;
        headPos = new float[3];
        headQuat = new float[4];
        rightHand = new HandMessage();
        leftHand = new HandMessage();
        trajectoryEdit = new TrajectoryEditMessage();  // 初始化
    }

    // Transform the head and hand poses to the calibrated align space
    public void TransformToAlignSpace()
    {
        if (Calibration.instance)
        {
            Vector3 vector3 = Calibration.instance.GetPosition(new Vector3(headPos[0], headPos[1], headPos[2]));
            headPos[0] = vector3.x;
            headPos[1] = vector3.y;
            headPos[2] = vector3.z;
            Quaternion quaternion = Calibration.instance.GetRotation(new Quaternion(headQuat[1], headQuat[2], headQuat[3], headQuat[0]));
            headQuat[0] = quaternion.w;
            headQuat[1] = quaternion.x;
            headQuat[2] = quaternion.y;
            headQuat[3] = quaternion.z;

            rightHand.TransformToAlignSpace();
            leftHand.TransformToAlignSpace();
            trajectoryEdit.TransformToAlignSpace();  // 转换轨迹编辑数据
        }
    }
}

/*
Component for collecting the poses and commands of the VR controller 
and sending to the worksation via HTTP.
*/

public class VRController : MonoBehaviour
{
    public static VRController instance;
    public string ip; // The default IP of the workstation
    public int port; // The default port of the workstation
    HttpClient client = new HttpClient();
    public int Hz = 30; // The frequency at which the VR controller pose data is sent to the workstation

    public TMPro.TextMeshProUGUI showText;

    public MyKeyboard keyboard;
    
    public ChunkVisualizer chunkVisualizer; // action chunk visualization component
    public Transform ovrhead;

    public Transform controller_right;
    public Transform controller_left;
    
    [Header("射线选择设置")]
    public bool enableRaySelection = true;
    public float rayLength = 10.0f;
    public Color rayColor = Color.green;
    public Color rayHitColor = Color.red;
    public LayerMask raycastMask = ~0;  // 默认检测所有层
    
    private LineRenderer leftRay;
    private LineRenderer rightRay;
    
    // 改进：只维护状态值，不维护GameObject引用
    private int hoveredPointIndex = -1;  // -1表示没有悬停
    private int selectedPointIndex = -1;  // -1表示没有选中
    private bool isEditingTrajectory = false;

    public static Message message;
    public bool LRinverse = false;

    protected void Start()
    {
        instance = this;
        showText.transform.parent.GetChild(1).GetComponent<TMPro.TextMeshProUGUI>().text = ip;
        message = new Message();
        Time.fixedDeltaTime = 1f / Hz;
        
        // 初始化射线可视化
        InitializeRays();
    }
    
    void InitializeRays()
    {
        // 左手射线
        GameObject leftRayGO = new GameObject("LeftControllerRay");
        leftRayGO.transform.SetParent(controller_left);
        leftRay = leftRayGO.AddComponent<LineRenderer>();
        ConfigureRay(leftRay);
        leftRay.enabled = false;  // 暂时不启用左手射线
        
        // 右手射线
        GameObject rightRayGO = new GameObject("RightControllerRay");
        rightRayGO.transform.SetParent(controller_right);
        rightRay = rightRayGO.AddComponent<LineRenderer>();
        ConfigureRay(rightRay);
    }
    
    void ConfigureRay(LineRenderer ray)
    {
        ray.startWidth = 0.002f;
        ray.endWidth = 0.002f;
        ray.material = new Material(Shader.Find("Sprites/Default"));
        ray.startColor = rayColor;
        ray.endColor = rayColor;
        ray.positionCount = 2;
        ray.enabled = enableRaySelection;
    }

    private void FixedUpdate()
    {
        CollectAndSend();
    }

    bool calibrationMode = false;
    bool cold = false;

    // Toggle calibration mode safely with a cooldown
    async void SwitchMode()
    {
        if (cold) return;
        cold = true;
        calibrationMode = !calibrationMode;
        Calibration.instance.SwitchAlign(calibrationMode);
        await Task.Delay(500);
        cold = false;
    }

    /* 已禁用 - ClearImage 功能
    async void ClearImage()
    {
        if (cold) return;
        cold = true;
        VisualizationServer.instance.ClearImage();
        await Task.Delay(500);
        cold = false;
    }
    */

    public void Update()
    {
        keyboard.transform.position = controller_right.position - new Vector3(0, 0.2f, 0);
        keyboard.transform.LookAt(Camera.main.transform);

        // switch between calibration mode and normal mode with long press of A+X buttons
        if (OVRInput.Get(OVRInput.RawButton.X) && OVRInput.Get(OVRInput.RawButton.A))
        {
            SwitchMode();
        }

        /* 已禁用 - 清除图像功能
        // clear all images on the workstation with long press of B+Y buttons
        if (OVRInput.Get(OVRInput.RawButton.Y) && OVRInput.Get(OVRInput.RawButton.B))
        {
            ClearImage();
        }
        */
        
        if (calibrationMode) return;

        // Toggle the keyboard display with left hand controller's thumbstick press
        if (OVRInput.GetDown(OVRInput.RawButton.LThumbstick))
        {
            keyboard.gameObject.SetActive(!keyboard.gameObject.activeSelf);
        }
        
        // 射线选择逻辑
        if (enableRaySelection)
        {
            UpdateRaySelection();
        }
        
        // 轨迹编辑模式控制 - 右手扳机
        if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
        {
            HandleTrajectorySelection();
        }
        
        // 按住扳机时更新轨迹点位置（在FixedUpdate的CollectAndSend中发送）
        if (OVRInput.Get(OVRInput.RawButton.RIndexTrigger) && isEditingTrajectory)
        {
            UpdateTrajectoryEditData();
        }
        
        // 松开扳机停止编辑
        if (OVRInput.GetUp(OVRInput.RawButton.RIndexTrigger))
        {
            StopTrajectoryEditing();
        }
    }

    void UpdateRaySelection()
    {
        // 右手控制器射线检测
        Ray ray = new Ray(controller_right.position, controller_right.forward);
        RaycastHit hit;
        
        // 清除之前的悬停状态（通过ChunkVisualizer更新视觉）
        if (hoveredPointIndex >= 0 && chunkVisualizer != null)
        {
            chunkVisualizer.SetPointHovered(hoveredPointIndex, false);
            hoveredPointIndex = -1;
        }
        
        if (Physics.Raycast(ray, out hit, rayLength, raycastMask))
        {
            // 检查是否击中轨迹点
            TrajectoryPointData pointData = hit.collider.GetComponent<TrajectoryPointData>();
            if (pointData != null)
            {
                hoveredPointIndex = pointData.pointIndex;
                if (chunkVisualizer != null)
                {
                    chunkVisualizer.SetPointHovered(hoveredPointIndex, true);
                }
                
                // 更新射线颜色
                rightRay.startColor = rayHitColor;
                rightRay.endColor = rayHitColor;
                
                // 更新射线终点到击中点
                rightRay.SetPosition(0, controller_right.position);
                rightRay.SetPosition(1, hit.point);
                return;
            }
        }
        
        // 未击中任何点，显示完整射线
        rightRay.startColor = rayColor;
        rightRay.endColor = rayColor;
        rightRay.SetPosition(0, controller_right.position);
        rightRay.SetPosition(1, controller_right.position + controller_right.forward * rayLength);
    }
    
    void HandleTrajectorySelection()
    {
        if (hoveredPointIndex >= 0)
        {
            // 取消之前选中的点（如果有）
            if (selectedPointIndex >= 0 && selectedPointIndex != hoveredPointIndex && chunkVisualizer != null)
            {
                chunkVisualizer.SetPointSelected(selectedPointIndex, false);
            }
            
            // 选中新点
            selectedPointIndex = hoveredPointIndex;
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetPointSelected(selectedPointIndex, true);
            }
            isEditingTrajectory = true;
            
            Debug.Log($"选中轨迹点: {selectedPointIndex}");
        }
    }
    
    void UpdateTrajectoryEditData()
    {
        if (selectedPointIndex >= 0)
        {
            // 更新Message中的编辑信息（原始VR空间坐标）
            message.trajectoryEdit.isEditing = true;
            message.trajectoryEdit.selectedPointIndex = selectedPointIndex;
            
            // 记录控制器位姿（原始坐标，稍后在CollectAndSend中通过TransformToAlignSpace转换）
            message.trajectoryEdit.editedPointPos[0] = controller_right.position.x;
            message.trajectoryEdit.editedPointPos[1] = controller_right.position.y;
            message.trajectoryEdit.editedPointPos[2] = controller_right.position.z;
            
            message.trajectoryEdit.editedPointQuat[0] = controller_right.rotation.w;
            message.trajectoryEdit.editedPointQuat[1] = controller_right.rotation.x;
            message.trajectoryEdit.editedPointQuat[2] = controller_right.rotation.y;
            message.trajectoryEdit.editedPointQuat[3] = controller_right.rotation.z;
        }
    }
    
    void StopTrajectoryEditing()
    {
        if (selectedPointIndex >= 0)
        {
            Debug.Log($"停止编辑轨迹点: {selectedPointIndex}");
            
            // 取消选中状态，点变回白色
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetPointSelected(selectedPointIndex, false);
            }
            
            // 清除编辑状态和选中索引
            message.trajectoryEdit.isEditing = false;
            message.trajectoryEdit.selectedPointIndex = -1;
            isEditingTrajectory = false;
            selectedPointIndex = -1; 
        }
    }

    public void CollectAndSend()
    {
        message.rightHand.wristPos[0] = controller_right.position.x;
        message.rightHand.wristPos[1] = controller_right.position.y;
        message.rightHand.wristPos[2] = controller_right.position.z;

        message.rightHand.wristQuat[0] = controller_right.rotation.w;
        message.rightHand.wristQuat[1] = controller_right.rotation.x;
        message.rightHand.wristQuat[2] = controller_right.rotation.y;
        message.rightHand.wristQuat[3] = controller_right.rotation.z;

        message.leftHand.wristPos[0] = controller_left.position.x;
        message.leftHand.wristPos[1] = controller_left.position.y;
        message.leftHand.wristPos[2] = controller_left.position.z;

        message.leftHand.wristQuat[0] = controller_left.rotation.w;
        message.leftHand.wristQuat[1] = controller_left.rotation.x;
        message.leftHand.wristQuat[2] = controller_left.rotation.y;
        message.leftHand.wristQuat[3] = controller_left.rotation.z;

        message.leftHand.triggerState = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);
        message.rightHand.triggerState = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger);

        message.leftHand.buttonState[0] = OVRInput.Get(OVRInput.RawButton.Y);
        message.leftHand.buttonState[1] = OVRInput.Get(OVRInput.RawButton.X);
        message.leftHand.buttonState[2] = OVRInput.Get(OVRInput.RawButton.LThumbstick);
        message.leftHand.buttonState[3] = OVRInput.Get(OVRInput.RawButton.LIndexTrigger);
        message.leftHand.buttonState[4] = OVRInput.Get(OVRInput.RawButton.LHandTrigger);

        message.rightHand.buttonState[0] = OVRInput.Get(OVRInput.RawButton.B);
        message.rightHand.buttonState[1] = OVRInput.Get(OVRInput.RawButton.A);
        message.rightHand.buttonState[2] = OVRInput.Get(OVRInput.RawButton.RThumbstick);
        message.rightHand.buttonState[3] = OVRInput.Get(OVRInput.RawButton.RIndexTrigger);
        message.rightHand.buttonState[4] = OVRInput.Get(OVRInput.RawButton.RHandTrigger);

        if (LRinverse)
        {
            var temp = message.leftHand;
            message.leftHand = message.rightHand;
            message.rightHand = temp;
        }

        message.headPos[0] = ovrhead.position.x;
        message.headPos[1] = ovrhead.position.y;
        message.headPos[2] = ovrhead.position.z;
        message.headQuat[0] = ovrhead.rotation.w;
        message.headQuat[1] = ovrhead.rotation.x;
        message.headQuat[2] = ovrhead.rotation.y;
        message.headQuat[3] = ovrhead.rotation.z;

        message.timestamp = Time.time;

        //transform to align space
        message.TransformToAlignSpace();

        string mes = JsonUtility.ToJson(message);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(mes);
        string url = $"http://{ip}:{port}/unity";
        var content = new ByteArrayContent(bodyRaw);
        client.PostAsync(url, content);
    }

    public void RefreshIP(string ip)
    {
        this.ip = ip;
    }
}
