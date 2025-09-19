using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
Test: CREATE A TrajectoryVisualizer CONSISTS OF 8 POINTS
EACH POINT HAS A SPHERE, 3 AXES, AND CONNECTING LINES
*/
public class TrajectoryVisualizer : MonoBehaviour
{
    [Header("轨迹设置")]
    public bool showTrajectory = true;
    public Transform[] trajectoryPoints = new Transform[8];
    public float pointSize = 0.02f;
    public float axisLength = 0.1f; 
    public float lineWidth = 0.005f;
    
    [Header("编辑器设置")]
    public bool createDefaultPoints = false; // Create default Points in Unity Editor
    public bool updateVisualization = false; // Update trajectory visualization in Play Mode
    
    [Header("参考对象")]
    public Transform userHead; 
    
    [Header("材质设置")]
    public Material whiteMaterial; // trajectory points and lines
    public Material redMaterial;   // x-axis
    public Material greenMaterial; // y-axis
    public Material blueMaterial;  //z-axis
    
    private GameObject trajectoryContainer;
    private List<GameObject> pointObjects = new List<GameObject>();
    private List<GameObject> lineObjects = new List<GameObject>();
    private List<GameObject> axisObjects = new List<GameObject>();
    
    void Start()
    {
        if (userHead == null)
        {
            Debug.LogError("TrajectoryVisualizer: uesrHead not defined.");
            return;
        }

        CreateMaterialsIfNeeded();

        if (AreTrajectoryPointsEmpty())
        {
            SetDefaultTrajectoryPoints();
        }
        
        CreateTrajectoryVisualization();
    }
    
    bool AreTrajectoryPointsEmpty()
    {
        for (int i = 0; i < trajectoryPoints.Length; i++)
        {
            if (trajectoryPoints[i] != null)
                return false;
        }
        return true;
    }
    
    void Update()
    {
        if (trajectoryContainer != null)
        {
            trajectoryContainer.SetActive(showTrajectory);
        }
        
        // Realtime Update in Editor
        #if UNITY_EDITOR
        HandleEditorUpdates();
        #endif
    }
    
    #if UNITY_EDITOR
    void HandleEditorUpdates()
    {
        if (createDefaultPoints)
        {
            createDefaultPoints = false;
            CreateDefaultTrajectoryPointsInEditor();
        }
        
        if (updateVisualization)
        {
            updateVisualization = false;
            UpdateTrajectoryVisualization();
        }
    }
    
    // Create default trajectory points in an arc in front of the user (Editor only)
    void CreateDefaultTrajectoryPointsInEditor()
    {
        if (userHead == null)
        {
            Debug.LogWarning("TrajectoryVisualizer: 请先设置userHead引用");
            return;
        }
        
        ClearExistingTrajectoryPoints();
        
        Vector3 center = userHead.position + userHead.forward * 1.5f;
        float radius = 0.5f;
        
        for (int i = 0; i < 8; i++)
        {
            GameObject pointTransform = new GameObject($"TrajectoryPoint_{i}");
            trajectoryPoints[i] = pointTransform.transform;
            trajectoryPoints[i].SetParent(this.transform);
            
            float angle = (i / 7f) * Mathf.PI;
            Vector3 localPos = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * 0.3f,
                0
            );
            
            trajectoryPoints[i].position = center + userHead.right * localPos.x + userHead.up * localPos.y;
            
            Vector3 lookDirection = (center - trajectoryPoints[i].position).normalized;
            trajectoryPoints[i].rotation = Quaternion.LookRotation(lookDirection, userHead.up);
            
            if (!UnityEditor.EditorApplication.isPlaying)
            {
                UnityEditor.Undo.RegisterCreatedObjectUndo(pointTransform, "Create Trajectory Point");
            }
        }
        
        Debug.Log("TrajectoryVisualizer: 已创建8个默认轨迹点，您现在可以在Scene中编辑它们的位置");
    }
    
    void ClearExistingTrajectoryPoints()
    {
        for (int i = 0; i < trajectoryPoints.Length; i++)
        {
            if (trajectoryPoints[i] != null)
            {
                if (UnityEditor.EditorApplication.isPlaying)
                {
                    DestroyImmediate(trajectoryPoints[i].gameObject);
                }
                else
                {
                    UnityEditor.Undo.DestroyObjectImmediate(trajectoryPoints[i].gameObject);
                }
                trajectoryPoints[i] = null;
            }
        }
    }
    #endif
    
    // initialize materials if not assigned
    void CreateMaterialsIfNeeded()
    {
        if (whiteMaterial == null)
        {
            whiteMaterial = CreateMaterial(Color.white);
        }
        if (redMaterial == null)
        {
            redMaterial = CreateMaterial(Color.red);
        }
        if (greenMaterial == null)
        {
            greenMaterial = CreateMaterial(Color.green);
        }
        if (blueMaterial == null)
        {
            blueMaterial = CreateMaterial(Color.blue);
        }
    }

    Material CreateMaterial(Color color)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0.2f);
        return mat;
    }
    
    // set default trajectory points in an arc in front of the user (runtime only)
    void SetDefaultTrajectoryPoints()
    {
        if (userHead == null) return;
        
        Vector3 center = userHead.position + userHead.forward * 1.5f;
        float radius = 0.5f;
        
        for (int i = 0; i < 8; i++)
        {
            // PlayMode: Create only if not exists
            if (trajectoryPoints[i] == null)
            {
                GameObject pointTransform = new GameObject($"TrajectoryPoint_{i}");
                trajectoryPoints[i] = pointTransform.transform;
                trajectoryPoints[i].SetParent(this.transform);
            }
            
            float angle = (i / 7f) * Mathf.PI;
            Vector3 localPos = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * 0.3f,
                0
            );
            
            // transform to world coordinates
            trajectoryPoints[i].position = center + userHead.right * localPos.x + userHead.up * localPos.y;
            
            // set rotation of trajectory points to face center
            Vector3 lookDirection = (center - trajectoryPoints[i].position).normalized;
            trajectoryPoints[i].rotation = Quaternion.LookRotation(lookDirection, userHead.up);
        }
    }
    
    // viusalization 
    void CreateTrajectoryVisualization()
    {
        trajectoryContainer = new GameObject("TrajectoryVisualization");
        trajectoryContainer.transform.SetParent(this.transform);
        
        CreateTrajectoryPoints();
        CreateConnectionLines();
        CreateCoordinateAxes();
    }
    
    void CreateTrajectoryPoints()
    {
        for (int i = 0; i < trajectoryPoints.Length; i++)
        {
            if (trajectoryPoints[i] == null) continue;
            
            // Create point sphere
            GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = $"Point_{i}";
            point.transform.SetParent(trajectoryContainer.transform);
            point.transform.position = trajectoryPoints[i].position;
            point.transform.localScale = Vector3.one * pointSize;

            Renderer renderer = point.GetComponent<Renderer>();
            renderer.material = whiteMaterial;

            DestroyImmediate(point.GetComponent<Collider>());
            
            pointObjects.Add(point);
        }
    }
    
    void CreateConnectionLines()
    {
        for (int i = 0; i < trajectoryPoints.Length - 1; i++)
        {
            if (trajectoryPoints[i] == null || trajectoryPoints[i + 1] == null) continue;
            
            GameObject line = CreateLine(trajectoryPoints[i].position, trajectoryPoints[i + 1].position, $"Line_{i}_{i + 1}");
            lineObjects.Add(line);
        }
    }
    
    void CreateCoordinateAxes()
    {
        for (int i = 0; i < trajectoryPoints.Length; i++)
        {
            if (trajectoryPoints[i] == null) continue;
            
            Transform point = trajectoryPoints[i];
            
            // red: x-axis
            Vector3 xEnd = point.position + point.right * axisLength;
            GameObject xAxis = CreateLine(point.position, xEnd, $"XAxis_{i}");
            xAxis.GetComponent<Renderer>().material = redMaterial;
            axisObjects.Add(xAxis);
            
            // green: y-axis
            Vector3 yEnd = point.position + point.up * axisLength;
            GameObject yAxis = CreateLine(point.position, yEnd, $"YAxis_{i}");
            yAxis.GetComponent<Renderer>().material = greenMaterial;
            axisObjects.Add(yAxis);
            
            // blue: z-axis
            Vector3 zEnd = point.position + point.forward * axisLength;
            GameObject zAxis = CreateLine(point.position, zEnd, $"ZAxis_{i}");
            zAxis.GetComponent<Renderer>().material = blueMaterial;
            axisObjects.Add(zAxis);
        }
    }
    
    // Create a cylinder between start and end points
    GameObject CreateLine(Vector3 start, Vector3 end, string name)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        line.name = name;
        line.transform.SetParent(trajectoryContainer.transform);
        
        Vector3 center = (start + end) / 2f;
        Vector3 direction = end - start;
        float length = direction.magnitude;
        
        line.transform.position = center;
        line.transform.rotation = Quaternion.LookRotation(direction);
        line.transform.Rotate(90, 0, 0); 
        
        line.transform.localScale = new Vector3(lineWidth * 2f, length / 2f, lineWidth * 2f);
        Renderer renderer = line.GetComponent<Renderer>();
        renderer.material = whiteMaterial;
        DestroyImmediate(line.GetComponent<Collider>());
        
        return line;
    }
    
    public void SetTrajectoryVisible(bool visible)
    {
        showTrajectory = visible;
        if (trajectoryContainer != null)
        {
            trajectoryContainer.SetActive(visible);
        }
    }
    
    public void UpdateTrajectoryVisualization()
    {
        if (trajectoryContainer != null)
        {
            DestroyImmediate(trajectoryContainer);
        }

        pointObjects.Clear();
        lineObjects.Clear();
        axisObjects.Clear();

        CreateTrajectoryVisualization();
    }
    
    void OnDestroy()
    {
        if (trajectoryContainer != null)
        {
            DestroyImmediate(trajectoryContainer);
        }
    }

    // Show trajectory preview in editor
    void OnDrawGizmosSelected()
    {
        if (trajectoryPoints == null) return;
         
        // trajectory points
        Gizmos.color = Color.white;
        for (int i = 0; i < trajectoryPoints.Length; i++)
        {
            if (trajectoryPoints[i] != null)
            {
                Gizmos.DrawWireSphere(trajectoryPoints[i].position, pointSize);
            }
        }

        // connection lines
        Gizmos.color = Color.gray;
        for (int i = 0; i < trajectoryPoints.Length - 1; i++)
        {
            if (trajectoryPoints[i] != null && trajectoryPoints[i + 1] != null)
            {
                Gizmos.DrawLine(trajectoryPoints[i].position, trajectoryPoints[i + 1].position);
            }
        }
        
        // axes upon each point
        for (int i = 0; i < trajectoryPoints.Length; i++)
        {
            if (trajectoryPoints[i] != null)
            {
                Transform point = trajectoryPoints[i];
                
                Gizmos.color = Color.red;
                Gizmos.DrawRay(point.position, point.right * axisLength);

                Gizmos.color = Color.green;
                Gizmos.DrawRay(point.position, point.up * axisLength);

                Gizmos.color = Color.blue;
                Gizmos.DrawRay(point.position, point.forward * axisLength);
            }
        }
    }
}
