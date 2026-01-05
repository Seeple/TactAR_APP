using System;
using UnityEngine;
using System.Threading.Tasks;
using System.Text;
using System.Net.Http;
using System.Collections;

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

/// <summary>
/// 简化版 Message - 仅包含 timestamp 和 trajectory edit 信息
/// </summary>
[Serializable]
public class HandEditMessage
{
    public float timestamp;
    public TrajectoryEditMessage trajectoryEdit;
    
    public HandEditMessage()
    {
        timestamp = 0f;
        trajectoryEdit = new TrajectoryEditMessage();
    }
    
    public void TransformToAlignSpace()
    {
        if (Calibration.instance)
        {
            trajectoryEdit.TransformToAlignSpace();
        }
    }
}

/// <summary>
/// 手势控制的轨迹编辑器 - 碰撞检测版本
/// 使用 OVRHand 物理碰撞来选择和拖动轨迹点（而非射线）
/// </summary>
public class VRController : MonoBehaviour
{
    public static VRController instance;
    
    [Header("网络设置")]
    public string ip; // The default IP of the workstation
    public int port; // The default port of the workstation
    HttpClient client = new HttpClient();
    public int Hz = 30; // The frequency at which the VR controller pose data is sent to the workstation

    [Header("手部跟踪")]
    public OVRHand rightHand;  // 右手 OVRHand 组件
    public OVRHand leftHand;   // 左手 OVRHand 组件
    
    [Header("控制器（用于按键操作）")]
    public Transform controller_right;  // 右手控制器（保留用于按键）
    public Transform controller_left;   // 左手控制器（保留用于按键）
    
    [Header("场景引用")]
    public TMPro.TextMeshProUGUI showText;
    public MyKeyboard keyboard;
    public ChunkVisualizer chunkVisualizer; // action chunk visualization component
    public Transform ovrhead;
    
    [Header("碰撞检测设置")]
    public bool enableCollisionDetection = true;
    public Transform indexFingerTip;  // 食指尖端 Transform（从 OVRSkeleton 获取）
    public float collisionRadius = 0.02f;  // 碰撞检测半径
    public LayerMask collisionMask = ~0;  // 碰撞检测层
    
    [Header("捏合手势设置")]
    public float pinchThreshold = 0.7f;  // 捏合强度阈值（0-1）
    public float releaseThreshold = 0.3f; // 松开阈值
    
    [Header("可视化调试")]
    public bool showDebugSphere = true;  // 是否显示调试球体
    public Color debugSphereColor = Color.green;
    
    private GameObject debugSphere;  // 调试用的碰撞检测球体
    private int hoveredPointIndex = -1;
    private int selectedPointIndex = -1;
    private bool isEditingTrajectory = false;
    private bool wasPinching = false;  // 上一帧是否在捏合

    private HandEditMessage message;
    public bool LRinverse = false;
    
    bool calibrationMode = false;
    bool cold = false;

    protected void Start()
    {
        instance = this;
        showText.transform.parent.GetChild(1).GetComponent<TMPro.TextMeshProUGUI>().text = ip;
        message = new HandEditMessage();
        Time.fixedDeltaTime = 1f / Hz;
        
        // 初始化碰撞检测
        InitializeCollisionDetection();
    }
    
    /// <summary>
    /// 初始化碰撞检测系统
    /// 关键：创建一个跟随食指尖端的触发器球体
    /// </summary>
    void InitializeCollisionDetection()
    {
        // 尝试自动查找食指尖端
        if (indexFingerTip == null && rightHand != null)
        {
            OVRSkeleton skeleton = rightHand.GetComponent<OVRSkeleton>();
            if (skeleton != null)
            {
                // 等待骨骼初始化
                StartCoroutine(WaitForSkeletonInit(skeleton));
            }
            else
            {
                Debug.LogError("VRController: 未找到 OVRSkeleton 组件！");
            }
        }
        
        // 创建调试球体
        if (showDebugSphere)
        {
            debugSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            debugSphere.name = "IndexFingerCollisionDebug";
            debugSphere.transform.localScale = Vector3.one * collisionRadius * 2f;
            
            // 设置为半透明材质
            Renderer renderer = debugSphere.GetComponent<Renderer>();
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(debugSphereColor.r, debugSphereColor.g, debugSphereColor.b, 0.3f);
            mat.SetFloat("_Mode", 3); // Transparent mode
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            renderer.material = mat;
            
            // 移除默认的 Collider（我们用 Physics.OverlapSphere）
            DestroyImmediate(debugSphere.GetComponent<Collider>());
        }
    }
    
    /// <summary>
    /// 等待 OVRSkeleton 初始化完成
    /// OVRSkeleton 需要几帧才能完成骨骼数据加载
    /// </summary>
    IEnumerator WaitForSkeletonInit(OVRSkeleton skeleton)
    {
        // 等待骨骼初始化
        while (!skeleton.IsInitialized)
        {
            yield return null;
        }
        
        // 查找食指尖端骨骼
        foreach (var bone in skeleton.Bones)
        {
            if (bone.Id == OVRSkeleton.BoneId.Hand_IndexTip)
            {
                indexFingerTip = bone.Transform;
                Debug.Log("VRController: 成功找到食指尖端骨骼");
                break;
            }
        }
        
        if (indexFingerTip == null)
        {
            Debug.LogError("VRController: 无法找到食指尖端骨骼！");
        }
    }

    private void FixedUpdate()
    {
        CollectAndSend();
    }

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

    public void Update()
    {
        // 键盘位置更新（使用控制器位置）
        if (keyboard != null && controller_right != null)
        {
            keyboard.transform.position = controller_right.position - new Vector3(0, 0.2f, 0);
            keyboard.transform.LookAt(Camera.main.transform);
        }

        // === 保留的控制器按键操作 ===
        
        // X + A: 切换校准模式
        if (OVRInput.Get(OVRInput.RawButton.X) && OVRInput.Get(OVRInput.RawButton.A))
        {
            SwitchMode();
        }
        
        if (calibrationMode) return;

        // 左手摇杆: 切换键盘
        if (OVRInput.GetDown(OVRInput.RawButton.LThumbstick))
        {
            if (keyboard != null)
            {
                keyboard.gameObject.SetActive(!keyboard.gameObject.activeSelf);
            }
        }
        
        // === 手势控制的轨迹编辑（碰撞检测版本） ===
        
        if (rightHand != null && rightHand.IsDataValid && enableCollisionDetection)
        {
            UpdateCollisionDetection();
            HandlePinchGesture();
        }
        
        // 更新编辑数据
        if (isEditingTrajectory && rightHand != null)
        {
            UpdateTrajectoryEditData();
        }
    }

    /// <summary>
    /// 核心方法：使用物理碰撞检测与轨迹点的接触
    /// 关键实现：使用 Physics.OverlapSphere 检测食指尖端附近的碰撞体
    /// </summary>
    void UpdateCollisionDetection()
    {
        // 检查食指尖端是否可用
        if (indexFingerTip == null)
        {
            if (debugSphere != null) debugSphere.SetActive(false);
            return;
        }

        // 更新调试球体位置
        if (debugSphere != null)
        {
            debugSphere.SetActive(true);
            debugSphere.transform.position = indexFingerTip.position;
        }
        
        // 没有碰撞，清除悬停状态
        if (hoveredPointIndex >= 0 && chunkVisualizer != null)
        {
            chunkVisualizer.SetPointHovered(hoveredPointIndex, false);
            hoveredPointIndex = -1;
        }
        
        // 核心：使用 Physics.OverlapSphere 检测碰撞
        // 在食指尖端位置创建一个球形检测区域
        Collider[] hitColliders = Physics.OverlapSphere(
            indexFingerTip.position,
            collisionRadius,
            collisionMask
        );
        
        // 遍历所有碰撞的对象，找到最近的轨迹点
        TrajectoryPointData closestPoint = null;
        float closestDistance = float.MaxValue;
        
        foreach (Collider col in hitColliders)
        {
            TrajectoryPointData pointData = col.GetComponent<TrajectoryPointData>();
            if (pointData != null)
            {
                // 计算距离，选择最近的点
                float distance = Vector3.Distance(indexFingerTip.position, col.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPoint = pointData;
                }
            }
        }
        
        // 如果找到了碰撞的点，设置为悬停状态
        if (closestPoint != null)
        {
            hoveredPointIndex = closestPoint.pointIndex;
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetPointHovered(hoveredPointIndex, true);
            }
            
            // 调试球体变红表示接触
            if (debugSphere != null)
            {
                debugSphere.GetComponent<Renderer>().material.color = 
                    new Color(1f, 0f, 0f, 0.5f);  // 红色半透明
            }
        }
        else
        {
            // 没有接触，恢复绿色
            if (debugSphere != null)
            {
                debugSphere.GetComponent<Renderer>().material.color = 
                    new Color(debugSphereColor.r, debugSphereColor.g, debugSphereColor.b, 0.3f);
            }
        }
    }
    
    /// <summary>
    /// 处理捏合手势（逻辑与 VRHandController 相同）
    /// </summary>
    void HandlePinchGesture()
    {
        float pinchStrength = rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
        
        bool isPinchingNow = wasPinching ? 
            (pinchStrength > releaseThreshold) :
            (pinchStrength > pinchThreshold);
        
        if (isPinchingNow && !wasPinching)
        {
            HandleTrajectorySelection();
        }
        
        if (!isPinchingNow && wasPinching)
        {
            StopTrajectoryEditing();
        }
        
        wasPinching = isPinchingNow;
    }
    
    /// <summary>
    /// 选中轨迹点（捏合开始时触发）
    /// </summary>
    void HandleTrajectorySelection()
    {
        if (hoveredPointIndex >= 0)
        {
            if (selectedPointIndex >= 0 && selectedPointIndex != hoveredPointIndex && chunkVisualizer != null)
            {
                chunkVisualizer.SetPointSelected(selectedPointIndex, false);
            }
            
            selectedPointIndex = hoveredPointIndex;
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetPointSelected(selectedPointIndex, true);
            }
            isEditingTrajectory = true;
            
            Debug.Log($"[手势] 选中轨迹点: {selectedPointIndex}");
        }
    }
    
    /// <summary>
    /// 更新轨迹编辑数据
    /// 关键：使用食指尖端的位置，而非 PointerPose
    /// </summary>
    void UpdateTrajectoryEditData()
    {
        if (selectedPointIndex >= 0 && indexFingerTip != null)
        {
            message.trajectoryEdit.isEditing = true;
            message.trajectoryEdit.selectedPointIndex = selectedPointIndex;
            
            // 使用食指尖端的位姿（原始 VR 空间坐标）
            Vector3 pos = indexFingerTip.position;
            Quaternion rot = indexFingerTip.rotation;
            
            message.trajectoryEdit.editedPointPos[0] = pos.x;
            message.trajectoryEdit.editedPointPos[1] = pos.y;
            message.trajectoryEdit.editedPointPos[2] = pos.z;
            
            message.trajectoryEdit.editedPointQuat[0] = rot.w;
            message.trajectoryEdit.editedPointQuat[1] = rot.x;
            message.trajectoryEdit.editedPointQuat[2] = rot.y;
            message.trajectoryEdit.editedPointQuat[3] = rot.z;
        }
    }
    
    /// <summary>
    /// 停止轨迹编辑（松开捏合时触发）
    /// </summary>
    void StopTrajectoryEditing()
    {
        isEditingTrajectory = false;
        message.trajectoryEdit.isEditing = false;
        message.trajectoryEdit.selectedPointIndex = -1;
        
        if (selectedPointIndex >= 0)
        {
            Debug.Log($"[手势] 停止编辑点 {selectedPointIndex}，取消选中");
            
            // 取消选中状态，点变回白色
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetPointSelected(selectedPointIndex, false);
            }
            
            // 清除选中索引
            selectedPointIndex = -1;
        }
    }

    /// <summary>
    /// 收集并发送数据（仅发送 timestamp 和 trajectoryEdit）
    /// </summary>
    public void CollectAndSend()
    {
        message.timestamp = Time.time;
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
    
    void OnDestroy()
    {
        if (debugSphere != null)
        {
            DestroyImmediate(debugSphere);
        }
    }
}
