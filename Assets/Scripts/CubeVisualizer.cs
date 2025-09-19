using UnityEngine;

/*
在用户前方显示一个立方体，支持两种模式：
1. smoothFollow = true: 平滑跟随用户的位置和旋转
2. smoothFollow = false: 固定在空间中的某个位置
*/

public class CubeVisualizer : MonoBehaviour
{
    [Header("立方体设置")]
    public GameObject cubePrefab; // TODO: create a cube prefab
    public float distanceFromUser = 2.0f;
    public float cubeSize = 0.5f; 
    public Color cubeColor = Color.blue; 

    [Header("跟随设置")]
    public bool smoothFollow = true;
    public float followSpeed = 5.0f; 

    [Header("位置更新")]
    public float updateFrequency = 10.0f; 

    [Header("参考对象")]
    public Transform userHead; 
    

    private GameObject cubeInstance; 
    private Renderer cubeRenderer;
    private float lastUpdateTime = 0f; 
    private Vector3 fixedPosition; 
    private Quaternion fixedRotation; 
    private bool isFixedPositionSet = false; 

    void Start()
    {
        if (userHead == null)
        {
            Debug.LogError("CubeVisualizer: userHead未设置！请在Unity Editor中指定用户头部Transform");
            return;
        }

        CreateCube();
        
        if (!smoothFollow && userHead != null)
        {
            SetFixedPosition();
        }
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
            // Create from prefab
            cubeInstance = Instantiate(cubePrefab);
        }
        else
        {
            cubeInstance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeInstance.name = "UserCube";

            cubeInstance.transform.localScale = Vector3.one * cubeSize;

            cubeRenderer = cubeInstance.GetComponent<Renderer>();
            if (cubeRenderer != null)
            {
                cubeRenderer.material.color = cubeColor;
            }
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
            // fix mode: use fixed position and rotation
            if (!isFixedPositionSet)
            {
                SetFixedPosition();
            }
            
            cubeInstance.transform.position = fixedPosition;
            cubeInstance.transform.rotation = fixedRotation;
        }
    }

    // show/hide cube
    public void SetCubeVisible(bool visible)
    {
        if (cubeInstance != null)
        {
            cubeInstance.SetActive(visible);
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
    
    // change follow mode
    public void SetSmoothFollow(bool smooth)
    {
        smoothFollow = smooth;
        if (!smooth)
        {
            SetFixedPosition();
        }
        Debug.Log($"CubeVisualizer: 跟随模式设置为 {(smooth ? "平滑跟随" : "固定位置")}");
    }

    public void ResetFixedPosition()
    {
        SetFixedPosition();
    }

    public void SetDistanceFromUser(float newDistance)
    {
        distanceFromUser = newDistance;
        if (!smoothFollow)
        {
            SetFixedPosition();
        }
    }

    void OnDestroy()
    {
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
