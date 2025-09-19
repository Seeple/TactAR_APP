using System;
using UnityEngine;
using System.Threading.Tasks;
using System.Text;
using System.Net.Http;

[Serializable]
public class HandMessage
{
    public float[] wristPos;
    public float[] wristQuat;
    public float triggerState;
    public bool[] buttonState;
    public HandMessage()
    {
        wristPos = new float[3];//position of the hand
        wristQuat = new float[4];//quaternion of the hand
        buttonState = new bool[5];//buttonState of B(Y)/A(X)/Thumbstick/IndexTrigger/HandTrigger
    }

    public void TransformToAlignSpace()
    {
        if (Calibration.instance)
        {
            Vector3 vector3 = Calibration.instance.GetPosition(new Vector3(wristPos[0], wristPos[1], wristPos[2]));
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
    public Message()
    {
        timestamp = Time.time;
        headPos = new float[3];
        headQuat = new float[4];
        rightHand = new HandMessage();
        leftHand = new HandMessage();
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
    
    public CubeVisualizer cubeVisualizer; // Test: Visualize a cube in front of the user
    
    public TrajectoryVisualizer trajectoryVisualizer; // Test: Trajectory visualization component

    public Transform ovrhead;

    public Transform controller_right;
    public Transform controller_left;

    public static Message message;
    public bool LRinverse = false;

    protected void Start()
    {
        instance = this;
        showText.transform.parent.GetChild(1).GetComponent<TMPro.TextMeshProUGUI>().text = ip;
        message = new Message();
        Time.fixedDeltaTime = 1f / Hz;
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

    async void ClearImage()
    {
        if (cold) return;
        cold = true;
        VisualizationServer.instance.ClearImage();
        await Task.Delay(500);
        cold = false;
    }

    async void ToggleCubeFollowMode()
    {
        if (cold) return;
        cold = true;
        if (cubeVisualizer != null)
        {
            bool currentSmoothFollow = cubeVisualizer.smoothFollow;
            cubeVisualizer.SetSmoothFollow(!currentSmoothFollow);
            
            string mode = currentSmoothFollow ? "固定位置" : "平滑跟随";
            Debug.Log($"立方体模式切换为: {mode}");
        }
        await Task.Delay(500);
        cold = false;
    }

    public void Update()
    {
        keyboard.transform.position = controller_right.position - new Vector3(0, 0.2f, 0);
        keyboard.transform.LookAt(Camera.main.transform);


        if (OVRInput.Get(OVRInput.RawButton.X) && OVRInput.Get(OVRInput.RawButton.A))
        {
            SwitchMode();
        }
        if (OVRInput.Get(OVRInput.RawButton.Y) && OVRInput.Get(OVRInput.RawButton.B))
        {
            ClearImage();
        }
        if (calibrationMode) return;

        if (OVRInput.GetDown(OVRInput.RawButton.LThumbstick))
        {
            keyboard.gameObject.SetActive(!keyboard.gameObject.activeSelf);
        }
        
        // Use the right hand controller's thumbstick press to toggle cube display
        if (OVRInput.GetDown(OVRInput.RawButton.RThumbstick))
        {
            if (cubeVisualizer != null)
            {
                bool isActive = cubeVisualizer.gameObject.activeSelf;
                cubeVisualizer.SetCubeVisible(!isActive);
            }
        }
        
        // Change the follow mode of the cube with long press of B button + thumbstick press(Test)
        if (OVRInput.Get(OVRInput.RawButton.B) && OVRInput.GetDown(OVRInput.RawButton.RThumbstick))
        {
            if (cubeVisualizer != null && !cold)
            {
                ToggleCubeFollowMode();
            }
        }
        
        // Use the right hand controller's A button press to toggle trajectory display
        if (OVRInput.GetDown(OVRInput.RawButton.A))
        {
            if (trajectoryVisualizer != null)
            {
                bool isVisible = trajectoryVisualizer.showTrajectory;
                trajectoryVisualizer.SetTrajectoryVisible(!isVisible);
                Debug.Log($"轨迹显示切换为: {(!isVisible ? "显示" : "隐藏")}");
            }
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
