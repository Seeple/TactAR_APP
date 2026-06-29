using System;
using UnityEngine;
using System.Threading.Tasks;
using System.Text;
using System.Net.Http;
using System.Collections;

// Matches HandMes on the workstation side
[Serializable]
public class HandMessage
{
    public float[] wristPos;   // (x, y, z)
    public float[] wristQuat;  // (w, qx, qy, qz)
    public float triggerState;
    public bool[] buttonState; // (B/Y, A/X, joystick, trigger, side_trigger)
    public float[] handPosePos;   // Optional hand/fingertip pose for sanity checks
    public float[] handPoseQuat;  // (w, qx, qy, qz)
    public float handPinchState;
    public bool handPoseValid;

    public HandMessage()
    {
        wristPos = new float[3];
        wristQuat = new float[4];
        buttonState = new bool[5];
        handPosePos = new float[3];
        handPoseQuat = new float[4];
        handPoseQuat[0] = 1f;
        handPinchState = 0f;
        handPoseValid = false;
    }

    public HandMessage Clone()
    {
        HandMessage copy = new HandMessage();
        Array.Copy(wristPos, copy.wristPos, wristPos.Length);
        Array.Copy(wristQuat, copy.wristQuat, wristQuat.Length);
        copy.triggerState = triggerState;
        Array.Copy(buttonState, copy.buttonState, buttonState.Length);
        Array.Copy(handPosePos, copy.handPosePos, handPosePos.Length);
        Array.Copy(handPoseQuat, copy.handPoseQuat, handPoseQuat.Length);
        copy.handPinchState = handPinchState;
        copy.handPoseValid = handPoseValid;
        return copy;
    }

    public void TransformToAlignSpace()
    {
        if (Calibration.instance)
        {
            Vector3 p = Calibration.instance.GetPosition(new Vector3(wristPos[0], wristPos[1], wristPos[2]));
            wristPos[0] = p.x; wristPos[1] = p.y; wristPos[2] = p.z;
            Quaternion q = Calibration.instance.GetRotation(new Quaternion(wristQuat[1], wristQuat[2], wristQuat[3], wristQuat[0]));
            wristQuat[0] = q.w; wristQuat[1] = q.x; wristQuat[2] = q.y; wristQuat[3] = q.z;

            if (handPoseValid)
            {
                Vector3 hp = Calibration.instance.GetPosition(new Vector3(handPosePos[0], handPosePos[1], handPosePos[2]));
                handPosePos[0] = hp.x; handPosePos[1] = hp.y; handPosePos[2] = hp.z;
                Quaternion hq = Calibration.instance.GetRotation(new Quaternion(handPoseQuat[1], handPoseQuat[2], handPoseQuat[3], handPoseQuat[0]));
                handPoseQuat[0] = hq.w; handPoseQuat[1] = hq.x; handPoseQuat[2] = hq.y; handPoseQuat[3] = hq.z;
            }
        }
    }
}

// Matches TrajectoryEdit on the workstation side
[Serializable]
public class TrajectoryEditMessage
{
    public int selectedPointIndex = -1;
    public bool isEditing = false;
    public float[] editedPointPos;
    public float[] editedPointQuat;
    
    public TrajectoryEditMessage()
    {
        editedPointPos = new float[3];
        editedPointQuat = new float[4];
    }

    public TrajectoryEditMessage Clone()
    {
        TrajectoryEditMessage copy = new TrajectoryEditMessage();
        copy.selectedPointIndex = selectedPointIndex;
        copy.isEditing = isEditing;
        Array.Copy(editedPointPos, copy.editedPointPos, editedPointPos.Length);
        Array.Copy(editedPointQuat, copy.editedPointQuat, editedPointQuat.Length);
        return copy;
    }
    
    // Convert world-space pose to Calibration local space before sending
    public void TransformToAlignSpace()
    {
        if (Calibration.instance && isEditing)
        {
            Vector3 p = Calibration.instance.GetPosition(new Vector3(editedPointPos[0], editedPointPos[1], editedPointPos[2]));
            editedPointPos[0] = p.x; editedPointPos[1] = p.y; editedPointPos[2] = p.z;
            Quaternion q = Calibration.instance.GetRotation(new Quaternion(editedPointQuat[1], editedPointQuat[2], editedPointQuat[3], editedPointQuat[0]));
            editedPointQuat[0] = q.w; editedPointQuat[1] = q.x; editedPointQuat[2] = q.y; editedPointQuat[3] = q.z;
        }
    }
}

// Matches UnityMes on the workstation side
[Serializable]
public class HandEditMessage
{
    public float timestamp;
    public HandMessage leftHand;
    public HandMessage rightHand;
    public float[] headPos;   // (x, y, z)
    public float[] headQuat;  // (w, qx, qy, qz)
    public TrajectoryEditMessage trajectoryEdit;
    
    public HandEditMessage()
    {
        timestamp = 0f;
        headPos = new float[3];
        headQuat = new float[4];
        leftHand = new HandMessage();
        rightHand = new HandMessage();
        trajectoryEdit = new TrajectoryEditMessage();
    }

    public HandEditMessage Clone()
    {
        HandEditMessage copy = new HandEditMessage();
        copy.timestamp = timestamp;
        Array.Copy(headPos, copy.headPos, headPos.Length);
        Array.Copy(headQuat, copy.headQuat, headQuat.Length);
        copy.leftHand = leftHand.Clone();
        copy.rightHand = rightHand.Clone();
        copy.trajectoryEdit = trajectoryEdit.Clone();
        return copy;
    }
    
    // Convert all world-space poses to Calibration local space
    public void TransformToAlignSpace()
    {
        if (Calibration.instance)
        {
            Vector3 hp = Calibration.instance.GetPosition(new Vector3(headPos[0], headPos[1], headPos[2]));
            headPos[0] = hp.x; headPos[1] = hp.y; headPos[2] = hp.z;
            Quaternion hq = Calibration.instance.GetRotation(new Quaternion(headQuat[1], headQuat[2], headQuat[3], headQuat[0]));
            headQuat[0] = hq.w; headQuat[1] = hq.x; headQuat[2] = hq.y; headQuat[3] = hq.z;

            leftHand.TransformToAlignSpace();
            rightHand.TransformToAlignSpace();
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

    [Header("放大映射选择设置")]
    public bool enableMagnifiedMapSelection = true;
    public MagnifiedTrajectoryMap magnifiedTrajectoryMap;
    public float magnifiedSelectionRadius = 0.005f;

    [Header("选择稳定性")]
    public float hoverGraceSeconds = 0.15f;
    public float pinchSelectionRadiusMultiplier = 1.35f;
    
    [Header("捏合手势设置")]
    public float pinchThreshold = 0.85f;  // 捏合强度阈值（0-1）
    public float releaseThreshold = 0.5f; // 松开阈值

    [Header("编辑手部跟踪保护")]
    public float editPoseGraceSeconds = 0.08f;
    
    [Header("可视化调试")]
    public bool showDebugSphere = true;  // 是否显示调试球体
    public Color debugSphereColor = Color.green;
    
    private GameObject debugSphere;  // 调试用的碰撞检测球体
    private int hoveredPointIndex = -1;
    private float lastHoverTime = -1000f;
    private int selectedPointIndex = -1;
    private bool isEditingTrajectory = false;
    private bool wasPinching = false;  // 上一帧是否在捏合

    private bool useMagnifiedMapSelection = false;
    private bool useLastPointSelection = false;
    private bool actionChunkVisible = true;
    private bool ghostGrippersVisible = true;
    private bool hasLastValidEditPose = false;
    private Vector3 lastValidEditPosePosition = Vector3.zero;
    private Quaternion lastValidEditPoseRotation = Quaternion.identity;
    private float lastValidEditPoseTime = -1000f;
    private bool pendingTrajectoryEditStop = false;

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

        InitializeMagnifiedTrajectoryMap();

        UpdateDebugVisibility();
    }

    void InitializeMagnifiedTrajectoryMap()
    {
        if (magnifiedTrajectoryMap == null)
        {
            magnifiedTrajectoryMap = gameObject.AddComponent<MagnifiedTrajectoryMap>();
        }

        if (magnifiedTrajectoryMap.headTransform == null)
        {
            magnifiedTrajectoryMap.headTransform = ovrhead;
        }

        magnifiedTrajectoryMap.selectionRadius = GetMagnifiedSelectionRadius();
        magnifiedTrajectoryMap.Initialize();
        magnifiedTrajectoryMap.SetSource(chunkVisualizer);
        magnifiedTrajectoryMap.SetVisible(false);
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
        bool allowControllerButtons = (OVRInput.GetActiveController() & (OVRInput.Controller.LTouch | OVRInput.Controller.RTouch)) != 0;

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

        // Toggle action chunk visibility with X (no combo)
        if (allowControllerButtons && OVRInput.GetDown(OVRInput.RawButton.X) && !OVRInput.Get(OVRInput.RawButton.A))
        {
            actionChunkVisible = !actionChunkVisible;
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetActionChunkVisible(actionChunkVisible);
            }
        }

        // Toggle ghost gripper visibility with A (no combo)
        if (allowControllerButtons && OVRInput.GetDown(OVRInput.RawButton.A) && !OVRInput.Get(OVRInput.RawButton.X))
        {
            ghostGrippersVisible = !ghostGrippersVisible;
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetGhostGrippersVisible(ghostGrippersVisible);
            }
        }

        // Toggle last-point selection mode with Y button
        if (allowControllerButtons && OVRInput.GetDown(OVRInput.RawButton.Y))
        {
            useLastPointSelection = !useLastPointSelection;
            ClearHoverState();
            ClearSelectedState();
            UpdateDebugVisibility();
            Debug.Log($"VRController: Last-point selection = {useLastPointSelection}");
        }

        // Toggle selection mode with B button
        if (allowControllerButtons && OVRInput.GetDown(OVRInput.RawButton.B))
        {
            useMagnifiedMapSelection = !useMagnifiedMapSelection;
            ClearHoverState();
            ClearSelectedState();
            UpdateDebugVisibility();
            Debug.Log($"VRController: Selection mode = {(useMagnifiedMapSelection ? "MagnifiedMap" : "Collision")}");
        }

        // 左手摇杆: 切换键盘
        if (OVRInput.GetDown(OVRInput.RawButton.LThumbstick))
        {
            if (keyboard != null)
            {
                keyboard.gameObject.SetActive(!keyboard.gameObject.activeSelf);
            }
        }
        
        // === Trajectory selection ===

        if (rightHand != null && rightHand.IsDataValid)
        {
            if (useLastPointSelection)
            {
                UpdateLastPointHover();
                HandlePinchGesture();
            }
            else if (useMagnifiedMapSelection && enableMagnifiedMapSelection)
            {
                UpdateMagnifiedMapSelection();
                HandlePinchGesture();
            }
            else if (enableCollisionDetection)
            {
                UpdateCollisionDetection();
                HandlePinchGesture();
            }
        }
        else
        {
            ExpireHoverStateIfNeeded();
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
    float GetMagnifiedSelectionRadius()
    {
        return Mathf.Max(collisionRadius, magnifiedSelectionRadius);
    }

    float GetPinchSelectionRadius(float baseRadius)
    {
        return Mathf.Max(0.001f, baseRadius * Mathf.Max(1f, pinchSelectionRadiusMultiplier));
    }

    bool TryFindCollisionPoint(Vector3 worldPosition, float radius, out int pointIndex)
    {
        pointIndex = -1;

        Collider[] hitColliders = Physics.OverlapSphere(
            worldPosition,
            radius,
            collisionMask
        );

        float closestDistance = float.MaxValue;
        foreach (Collider col in hitColliders)
        {
            TrajectoryPointData pointData = col.GetComponent<TrajectoryPointData>();
            if (pointData == null)
            {
                pointData = col.GetComponentInParent<TrajectoryPointData>();
            }

            if (pointData == null)
            {
                continue;
            }

            float distance = Vector3.Distance(worldPosition, pointData.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                pointIndex = pointData.pointIndex;
            }
        }

        return pointIndex >= 0;
    }

    void SetHoverPoint(int pointIndex)
    {
        if (pointIndex < 0)
        {
            ClearHoverState();
            return;
        }

        if (hoveredPointIndex >= 0 && hoveredPointIndex != pointIndex)
        {
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetPointHovered(hoveredPointIndex, false);
            }
            if (magnifiedTrajectoryMap != null)
            {
                magnifiedTrajectoryMap.SetPointHovered(hoveredPointIndex, false);
            }
        }

        hoveredPointIndex = pointIndex;
        lastHoverTime = Time.time;

        if (chunkVisualizer != null)
        {
            chunkVisualizer.SetPointHovered(hoveredPointIndex, true);
        }
        if (magnifiedTrajectoryMap != null)
        {
            magnifiedTrajectoryMap.SetPointHovered(hoveredPointIndex, true);
        }
    }

    bool TryGetRecentHoveredPoint(out int pointIndex)
    {
        pointIndex = -1;

        if (hoveredPointIndex < 0)
        {
            return false;
        }

        if (Time.time - lastHoverTime > Mathf.Max(0f, hoverGraceSeconds))
        {
            return false;
        }

        pointIndex = hoveredPointIndex;
        return true;
    }

    void ExpireHoverStateIfNeeded()
    {
        int recentPointIndex;
        if (hoveredPointIndex >= 0 && !TryGetRecentHoveredPoint(out recentPointIndex))
        {
            ClearHoverState();
        }
    }

    void UpdateCollisionDetection()
    {
        // 检查食指尖端是否可用
        if (indexFingerTip == null)
        {
            if (debugSphere != null) debugSphere.SetActive(false);
            ExpireHoverStateIfNeeded();
            return;
        }

        // 更新调试球体位置
        if (debugSphere != null)
        {
            debugSphere.SetActive(true);
            debugSphere.transform.position = indexFingerTip.position;
            debugSphere.transform.localScale = Vector3.one * collisionRadius * 2f;
        }
        
        // 如果找到了碰撞的点，设置为悬停状态
        int closestIndex;
        if (TryFindCollisionPoint(indexFingerTip.position, collisionRadius, out closestIndex))
        {
            SetHoverPoint(closestIndex);
            
            // 调试球体变红表示接触
            if (debugSphere != null)
            {
                debugSphere.GetComponent<Renderer>().material.color = 
                    new Color(1f, 0f, 0f, 0.5f);  // 红色半透明
            }
        }
        else
        {
            ExpireHoverStateIfNeeded();

            // 没有接触，恢复绿色
            if (debugSphere != null)
            {
                debugSphere.GetComponent<Renderer>().material.color = 
                    new Color(debugSphereColor.r, debugSphereColor.g, debugSphereColor.b, 0.3f);
            }
        }
    }

    void UpdateMagnifiedMapSelection()
    {
        if (magnifiedTrajectoryMap == null)
        {
            if (debugSphere != null) debugSphere.SetActive(false);
            ClearHoverState();
            return;
        }

        if (indexFingerTip == null)
        {
            if (debugSphere != null) debugSphere.SetActive(false);
            ExpireHoverStateIfNeeded();
            return;
        }

        float selectionRadius = GetMagnifiedSelectionRadius();
        magnifiedTrajectoryMap.selectionRadius = selectionRadius;
        magnifiedTrajectoryMap.SetVisible(true);

        if (debugSphere != null)
        {
            debugSphere.SetActive(true);
            debugSphere.transform.position = indexFingerTip.position;
            debugSphere.transform.localScale = Vector3.one * selectionRadius * 2f;
        }

        int closestIndex;
        if (magnifiedTrajectoryMap.TryGetClosestPoint(indexFingerTip.position, selectionRadius, out closestIndex))
        {
            SetHoverPoint(closestIndex);

            if (debugSphere != null)
            {
                debugSphere.GetComponent<Renderer>().material.color =
                    new Color(1f, 0f, 0f, 0.5f);
            }
        }
        else
        {
            ExpireHoverStateIfNeeded();

            if (debugSphere != null)
            {
                debugSphere.GetComponent<Renderer>().material.color =
                    new Color(debugSphereColor.r, debugSphereColor.g, debugSphereColor.b, 0.3f);
            }
        }
    }

    void ClearHoverState()
    {
        if (hoveredPointIndex >= 0 && chunkVisualizer != null)
        {
            chunkVisualizer.SetPointHovered(hoveredPointIndex, false);
        }
        if (hoveredPointIndex >= 0 && magnifiedTrajectoryMap != null)
        {
            magnifiedTrajectoryMap.SetPointHovered(hoveredPointIndex, false);
        }
        hoveredPointIndex = -1;
        lastHoverTime = -1000f;
    }

    void UpdateDebugVisibility()
    {
        bool showMagnifiedMap = useMagnifiedMapSelection && enableMagnifiedMapSelection && !useLastPointSelection;
        bool showSphere = showDebugSphere && !useLastPointSelection;

        if (magnifiedTrajectoryMap != null)
        {
            magnifiedTrajectoryMap.SetVisible(showMagnifiedMap);
        }

        if (debugSphere != null)
        {
            debugSphere.SetActive(showSphere);
        }
    }

    void ClearSelectedState()
    {
        pendingTrajectoryEditStop = false;
        hasLastValidEditPose = false;
        if (magnifiedTrajectoryMap != null)
        {
            magnifiedTrajectoryMap.EndEdit();
        }

        if (selectedPointIndex >= 0 && chunkVisualizer != null)
        {
            chunkVisualizer.SetPointSelected(selectedPointIndex, false);
        }
        if (selectedPointIndex >= 0 && magnifiedTrajectoryMap != null)
        {
            magnifiedTrajectoryMap.SetPointSelected(selectedPointIndex, false);
        }
        selectedPointIndex = -1;
        isEditingTrajectory = false;
        ClearTrajectoryEditPayload();

        if (chunkVisualizer != null)
        {
            chunkVisualizer.SetSelectedGhost(-1, false);
        }
    }

    void UpdateLastPointHover()
    {
        if (chunkVisualizer == null) return;

        int lastIndex = chunkVisualizer.GetLastPointIndex();
        if (lastIndex < 0) return;

        SetHoverPoint(lastIndex);
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
    bool TryResolveSelectionPointForPinch(out int pointIndex)
    {
        pointIndex = -1;

        if (useLastPointSelection && chunkVisualizer != null)
        {
            pointIndex = chunkVisualizer.GetLastPointIndex();
            return pointIndex >= 0;
        }

        if (indexFingerTip != null)
        {
            if (useMagnifiedMapSelection && enableMagnifiedMapSelection && magnifiedTrajectoryMap != null)
            {
                float radius = GetPinchSelectionRadius(GetMagnifiedSelectionRadius());
                magnifiedTrajectoryMap.SetVisible(true);
                if (magnifiedTrajectoryMap.TryGetClosestPoint(indexFingerTip.position, radius, out pointIndex))
                {
                    return true;
                }
            }
            else if (enableCollisionDetection)
            {
                float radius = GetPinchSelectionRadius(collisionRadius);
                if (TryFindCollisionPoint(indexFingerTip.position, radius, out pointIndex))
                {
                    return true;
                }
            }
        }

        return TryGetRecentHoveredPoint(out pointIndex);
    }

    void HandleTrajectorySelection()
    {
        int pointIndex;
        if (!TryResolveSelectionPointForPinch(out pointIndex))
        {
            return;
        }

        SetHoverPoint(pointIndex);

        if (pointIndex >= 0)
        {
            if (selectedPointIndex >= 0 && selectedPointIndex != pointIndex && chunkVisualizer != null)
            {
                chunkVisualizer.SetPointSelected(selectedPointIndex, false);
            }
            if (selectedPointIndex >= 0 && selectedPointIndex != pointIndex && magnifiedTrajectoryMap != null)
            {
                magnifiedTrajectoryMap.SetPointSelected(selectedPointIndex, false);
            }
            
            selectedPointIndex = pointIndex;
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetPointSelected(selectedPointIndex, true);
                chunkVisualizer.SetSelectedGhost(selectedPointIndex, true);
            }
            if (magnifiedTrajectoryMap != null)
            {
                magnifiedTrajectoryMap.SetPointSelected(selectedPointIndex, true);
                if (ShouldUseMagnifiedPoseMapping())
                {
                    magnifiedTrajectoryMap.BeginEdit(selectedPointIndex);
                }
            }
            hasLastValidEditPose = false;
            pendingTrajectoryEditStop = false;
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
        if (useLastPointSelection && chunkVisualizer != null)
        {
            int lastIndex = chunkVisualizer.GetLastPointIndex();
            if (lastIndex >= 0)
            {
                selectedPointIndex = lastIndex;
            }
        }

        if (selectedPointIndex >= 0)
        {
            Vector3 pos;
            Quaternion rot;
            if (!TryGetFreshEffectiveIndexTipPose(out pos, out rot) &&
                !TryGetRecentEditPose(out pos, out rot))
            {
                ClearTrajectoryEditPayload();
                return;
            }

            WriteTrajectoryEditPose(selectedPointIndex, pos, rot);

            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetSelectedGhost(selectedPointIndex, isEditingTrajectory);
            }
        }
    }

    bool ShouldUseMagnifiedPoseMapping()
    {
        return useMagnifiedMapSelection &&
               enableMagnifiedMapSelection &&
               !useLastPointSelection &&
               magnifiedTrajectoryMap != null;
    }

    bool TryMapMagnifiedPose(Vector3 sourcePosition, Quaternion sourceRotation, out Vector3 mappedPosition, out Quaternion mappedRotation)
    {
        mappedPosition = sourcePosition;
        mappedRotation = sourceRotation;

        if (!ShouldUseMagnifiedPoseMapping())
        {
            return false;
        }

        return magnifiedTrajectoryMap.TryMapProxyPoseToTrajectoryWorld(sourcePosition, sourceRotation, out mappedPosition, out mappedRotation);
    }

    bool IsFinitePose(Vector3 position, Quaternion rotation)
    {
        return !(float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z) ||
                 float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z) ||
                 float.IsNaN(rotation.x) || float.IsNaN(rotation.y) || float.IsNaN(rotation.z) || float.IsNaN(rotation.w) ||
                 float.IsInfinity(rotation.x) || float.IsInfinity(rotation.y) || float.IsInfinity(rotation.z) || float.IsInfinity(rotation.w));
    }

    bool TryGetFreshEffectiveIndexTipPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (rightHand == null || !rightHand.IsDataValid || indexFingerTip == null)
        {
            return false;
        }

        position = indexFingerTip.position;
        rotation = indexFingerTip.rotation;
        if (!IsFinitePose(position, rotation))
        {
            return false;
        }

        Vector3 mappedPosition;
        Quaternion mappedRotation;
        if (TryMapMagnifiedPose(position, rotation, out mappedPosition, out mappedRotation))
        {
            position = mappedPosition;
            rotation = mappedRotation;
        }

        if (!IsFinitePose(position, rotation))
        {
            return false;
        }

        lastValidEditPosePosition = position;
        lastValidEditPoseRotation = rotation;
        lastValidEditPoseTime = Time.time;
        hasLastValidEditPose = true;

        return true;
    }

    bool TryGetRecentEditPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!hasLastValidEditPose)
        {
            return false;
        }

        if (Time.time - lastValidEditPoseTime > Mathf.Max(0f, editPoseGraceSeconds))
        {
            return false;
        }

        position = lastValidEditPosePosition;
        rotation = lastValidEditPoseRotation;
        return true;
    }

    void WriteTrajectoryEditPose(int pointIndex, Vector3 position, Quaternion rotation)
    {
        message.trajectoryEdit.isEditing = true;
        message.trajectoryEdit.selectedPointIndex = pointIndex;
        message.trajectoryEdit.editedPointPos[0] = position.x;
        message.trajectoryEdit.editedPointPos[1] = position.y;
        message.trajectoryEdit.editedPointPos[2] = position.z;
        message.trajectoryEdit.editedPointQuat[0] = rotation.w;
        message.trajectoryEdit.editedPointQuat[1] = rotation.x;
        message.trajectoryEdit.editedPointQuat[2] = rotation.y;
        message.trajectoryEdit.editedPointQuat[3] = rotation.z;
    }

    void ClearTrajectoryEditPayload()
    {
        message.trajectoryEdit.isEditing = false;
        message.trajectoryEdit.selectedPointIndex = -1;
    }

    void CompletePendingTrajectoryEditStop()
    {
        if (!pendingTrajectoryEditStop)
        {
            return;
        }

        pendingTrajectoryEditStop = false;
        ClearTrajectoryEditPayload();
    }
    
    /// <summary>
    /// 停止轨迹编辑（松开捏合时触发）
    /// </summary>
    void StopTrajectoryEditing()
    {
        int stoppedPointIndex = selectedPointIndex;
        bool queuedFinalEditPose = false;

        if (stoppedPointIndex >= 0)
        {
            Vector3 finalPos;
            Quaternion finalRot;
            if (TryGetFreshEffectiveIndexTipPose(out finalPos, out finalRot) ||
                TryGetRecentEditPose(out finalPos, out finalRot))
            {
                WriteTrajectoryEditPose(stoppedPointIndex, finalPos, finalRot);
                pendingTrajectoryEditStop = true;
                queuedFinalEditPose = true;
            }
        }

        isEditingTrajectory = false;
        if (!queuedFinalEditPose)
        {
            ClearTrajectoryEditPayload();
        }

        if (magnifiedTrajectoryMap != null)
        {
            magnifiedTrajectoryMap.EndEdit();
        }

        if (chunkVisualizer != null)
        {
            chunkVisualizer.SetSelectedGhost(-1, false);
        }
        
        if (selectedPointIndex >= 0)
        {
            Debug.Log($"[手势] 停止编辑点 {selectedPointIndex}，取消选中");
            
            // 取消选中状态，点变回白色
            if (chunkVisualizer != null)
            {
                chunkVisualizer.SetPointSelected(selectedPointIndex, false);
            }
            if (magnifiedTrajectoryMap != null)
            {
                magnifiedTrajectoryMap.SetPointSelected(selectedPointIndex, false);
            }
            
            // 清除选中索引
            selectedPointIndex = -1;
        }
    }

    void ClearHandPoseMessage(HandMessage handMessage)
    {
        if (handMessage == null) return;

        handMessage.handPoseValid = false;
        handMessage.handPinchState = 0f;
        handMessage.handPosePos[0] = 0f;
        handMessage.handPosePos[1] = 0f;
        handMessage.handPosePos[2] = 0f;
        handMessage.handPoseQuat[0] = 1f;
        handMessage.handPoseQuat[1] = 0f;
        handMessage.handPoseQuat[2] = 0f;
        handMessage.handPoseQuat[3] = 0f;
    }

    void UpdateHandPoseMessage(HandMessage handMessage, OVRHand sourceHand, Transform poseTransform, bool useEffectiveIndexPose = false)
    {
        ClearHandPoseMessage(handMessage);

        if (handMessage == null || sourceHand == null)
        {
            return;
        }

        handMessage.handPinchState = sourceHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
        if (poseTransform == null || !sourceHand.IsDataValid)
        {
            return;
        }

        Vector3 pos = poseTransform.position;
        Quaternion rot = poseTransform.rotation;
        if (useEffectiveIndexPose)
        {
            Vector3 mappedPos;
            Quaternion mappedRot;
            if (TryMapMagnifiedPose(pos, rot, out mappedPos, out mappedRot))
            {
                pos = mappedPos;
                rot = mappedRot;
            }
        }

        handMessage.handPosePos[0] = pos.x;
        handMessage.handPosePos[1] = pos.y;
        handMessage.handPosePos[2] = pos.z;
        handMessage.handPoseQuat[0] = rot.w;
        handMessage.handPoseQuat[1] = rot.x;
        handMessage.handPoseQuat[2] = rot.y;
        handMessage.handPoseQuat[3] = rot.z;
        handMessage.handPoseValid = true;
    }

    // Collect all pose/button data and send to workstation at fixed Hz
    public void CollectAndSend()
    {
        message.timestamp = Time.time;

        // Head pose
        if (ovrhead != null)
        {
            message.headPos[0] = ovrhead.position.x;
            message.headPos[1] = ovrhead.position.y;
            message.headPos[2] = ovrhead.position.z;
            message.headQuat[0] = ovrhead.rotation.w;
            message.headQuat[1] = ovrhead.rotation.x;
            message.headQuat[2] = ovrhead.rotation.y;
            message.headQuat[3] = ovrhead.rotation.z;
        }

        // Right controller pose and buttons
        OVRInput.Controller rightController = LRinverse ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        OVRInput.Controller leftController  = LRinverse ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;

        if (controller_right != null)
        {
            message.rightHand.wristPos[0] = controller_right.position.x;
            message.rightHand.wristPos[1] = controller_right.position.y;
            message.rightHand.wristPos[2] = controller_right.position.z;
            message.rightHand.wristQuat[0] = controller_right.rotation.w;
            message.rightHand.wristQuat[1] = controller_right.rotation.x;
            message.rightHand.wristQuat[2] = controller_right.rotation.y;
            message.rightHand.wristQuat[3] = controller_right.rotation.z;
        }
        message.rightHand.triggerState = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, rightController);
        message.rightHand.buttonState[0] = OVRInput.Get(OVRInput.RawButton.B);
        message.rightHand.buttonState[1] = OVRInput.Get(OVRInput.RawButton.A);
        message.rightHand.buttonState[2] = OVRInput.Get(OVRInput.RawButton.RThumbstick);
        message.rightHand.buttonState[3] = OVRInput.Get(OVRInput.RawButton.RIndexTrigger);
        message.rightHand.buttonState[4] = OVRInput.Get(OVRInput.RawButton.RHandTrigger);

        // Left controller pose and buttons
        if (controller_left != null)
        {
            message.leftHand.wristPos[0] = controller_left.position.x;
            message.leftHand.wristPos[1] = controller_left.position.y;
            message.leftHand.wristPos[2] = controller_left.position.z;
            message.leftHand.wristQuat[0] = controller_left.rotation.w;
            message.leftHand.wristQuat[1] = controller_left.rotation.x;
            message.leftHand.wristQuat[2] = controller_left.rotation.y;
            message.leftHand.wristQuat[3] = controller_left.rotation.z;
        }
        message.leftHand.triggerState = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, leftController);
        message.leftHand.buttonState[0] = OVRInput.Get(OVRInput.RawButton.Y);
        message.leftHand.buttonState[1] = OVRInput.Get(OVRInput.RawButton.X);
        message.leftHand.buttonState[2] = OVRInput.Get(OVRInput.RawButton.LThumbstick);
        message.leftHand.buttonState[3] = OVRInput.Get(OVRInput.RawButton.LIndexTrigger);
        message.leftHand.buttonState[4] = OVRInput.Get(OVRInput.RawButton.LHandTrigger);

        UpdateHandPoseMessage(message.rightHand, rightHand, indexFingerTip, true);
        UpdateHandPoseMessage(message.leftHand, leftHand, null);

        HandEditMessage outboundMessage = message.Clone();
        outboundMessage.TransformToAlignSpace();

        string mes = JsonUtility.ToJson(outboundMessage);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(mes);
        string url = $"http://{ip}:{port}/unity";
        var content = new ByteArrayContent(bodyRaw);
        client.PostAsync(url, content);

        CompletePendingTrajectoryEditStop();
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

    public bool TryGetIndexTipPose(out Vector3 position, out Quaternion rotation)
    {
        if (indexFingerTip != null)
        {
            position = indexFingerTip.position;
            rotation = indexFingerTip.rotation;
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    public bool TryGetEditedPointPose(out Vector3 position, out Quaternion rotation, out bool isEditing, out int selectedIndex)
    {
        if (message == null || message.trajectoryEdit == null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            isEditing = false;
            selectedIndex = -1;
            return false;
        }

        TrajectoryEditMessage edit = message.trajectoryEdit;
        isEditing = edit.isEditing;
        selectedIndex = edit.selectedPointIndex;
        position = new Vector3(edit.editedPointPos[0], edit.editedPointPos[1], edit.editedPointPos[2]);
        rotation = new Quaternion(edit.editedPointQuat[1], edit.editedPointQuat[2], edit.editedPointQuat[3], edit.editedPointQuat[0]);
        return true;
    }
}
