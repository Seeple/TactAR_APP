using System.Collections.Generic;
using UnityEngine;

public class MagnifiedTrajectoryMap : MonoBehaviour
{
    [Header("References")]
    public Transform headTransform;
    public ChunkVisualizer sourceVisualizer;

    [Header("Placement")]
    public Vector3 localOffset = new Vector3(0f, -0.05f, 0.32f);
    public float mapScale = 3.0f;

    [Header("Interaction")]
    public float selectionRadius = 0.045f;

    [Header("Visuals")]
    public float pointSize = 0.012f;
    public float lineWidth = 0.003f;
    public bool showAxes = false;
    public float axisLength = 0.055f;
    public Color pointColor = Color.white;
    public Color hoverColor = Color.cyan;
    public Color selectedColor = Color.yellow;
    public Color lineColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    public Color xAxisColor = Color.red;
    public Color yAxisColor = Color.green;
    public Color zAxisColor = Color.blue;

    private GameObject mapRoot;
    private readonly List<GameObject> pointObjects = new List<GameObject>();
    private readonly List<GameObject> lineObjects = new List<GameObject>();
    private readonly List<GameObject> axisObjects = new List<GameObject>();
    private readonly List<Vector3> trajectoryLocalPositions = new List<Vector3>();
    private readonly List<Quaternion> trajectoryLocalRotations = new List<Quaternion>();
    private readonly List<Vector3> mapLocalPositions = new List<Vector3>();

    private Material pointMaterial;
    private Material hoverMaterial;
    private Material selectedMaterial;
    private Material lineMaterial;
    private Material xAxisMaterial;
    private Material yAxisMaterial;
    private Material zAxisMaterial;

    private Vector3 trajectoryCenterLocal = Vector3.zero;
    private float activeScale = 1f;
    private int hoveredPointIndex = -1;
    private int selectedPointIndex = -1;
    private bool initialized = false;
    private bool visible = false;
    private bool subscribedToSource = false;
    private bool hasLockedWorldPosition = false;
    private Vector3 lockedWorldPosition = Vector3.zero;
    private bool editBasisLocked = false;
    private int lockedEditPointIndex = -1;
    private Vector3 lockedEditTrajectoryCenterLocal = Vector3.zero;
    private float lockedEditScale = 1f;
    private Matrix4x4 lockedEditMapWorldToLocalMatrix = Matrix4x4.identity;
    private Matrix4x4 lockedEditTrajectoryLocalToWorldMatrix = Matrix4x4.identity;
    private Vector3 lockedEditMapRootPosition = Vector3.zero;
    private Quaternion lockedEditMapRootRotation = Quaternion.identity;
    private Quaternion lockedEditTrajectoryRootRotation = Quaternion.identity;

    void Awake()
    {
        Initialize();
    }

    void Start()
    {
        Initialize();
        RefreshFromSourceSnapshot();
    }

    void LateUpdate()
    {
        EnsureHeadTransform();
        if (visible)
        {
            ApplyRootPose();
        }
    }

    void OnDestroy()
    {
        if (sourceVisualizer != null && subscribedToSource)
        {
            sourceVisualizer.TrajectoryUpdated -= OnTrajectoryUpdated;
        }
    }

    public void Initialize()
    {
        if (initialized)
        {
            EnsureHeadTransform();
            EnsureRoot();
            ApplyRootPose();
            return;
        }

        EnsureHeadTransform();
        CreateMaterials();
        EnsureRoot();
        SetSource(sourceVisualizer);
        initialized = true;
    }

    public void SetSource(ChunkVisualizer visualizer)
    {
        if (sourceVisualizer == visualizer && subscribedToSource)
        {
            return;
        }

        if (sourceVisualizer != null && subscribedToSource)
        {
            sourceVisualizer.TrajectoryUpdated -= OnTrajectoryUpdated;
            subscribedToSource = false;
        }

        sourceVisualizer = visualizer;

        if (sourceVisualizer != null)
        {
            sourceVisualizer.TrajectoryUpdated += OnTrajectoryUpdated;
            subscribedToSource = true;
            RefreshFromSourceSnapshot();
        }
    }

    public void SetVisible(bool isVisible)
    {
        bool wasVisible = visible;
        visible = isVisible;
        EnsureRoot();
        if (visible && !wasVisible)
        {
            RefreshPlacementFromHead();
        }
        else
        {
            ApplyRootPose();
        }

        if (mapRoot != null)
        {
            mapRoot.SetActive(visible);
        }
    }

    public bool IsVisible()
    {
        return visible;
    }

    public void RefreshPlacementFromHead()
    {
        EnsureHeadTransform();
        EnsureRoot();

        if (headTransform != null)
        {
            lockedWorldPosition = headTransform.TransformPoint(localOffset);
        }
        else
        {
            lockedWorldPosition = localOffset;
        }

        hasLockedWorldPosition = true;
        ApplyRootPose();
    }

    public bool TryGetClosestPoint(Vector3 worldPosition, float radius, out int pointIndex)
    {
        pointIndex = -1;

        if (!visible || mapRoot == null || mapLocalPositions.Count == 0)
        {
            return false;
        }

        Vector3 localPosition = mapRoot.transform.InverseTransformPoint(worldPosition);
        float bestDistance = float.MaxValue;
        float maxDistance = Mathf.Max(0.001f, radius);

        for (int i = 0; i < mapLocalPositions.Count; i++)
        {
            float distance = Vector3.Distance(localPosition, mapLocalPositions[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                pointIndex = i;
            }
        }

        if (pointIndex < 0 || bestDistance > maxDistance)
        {
            pointIndex = -1;
            return false;
        }

        return true;
    }

    public void BeginEdit(int pointIndex)
    {
        Initialize();
        EnsureRoot();
        ApplyRootPose();

        if (mapRoot == null || activeScale <= 0.0001f)
        {
            return;
        }

        lockedEditPointIndex = pointIndex;
        lockedEditTrajectoryCenterLocal = trajectoryCenterLocal;
        lockedEditScale = Mathf.Max(0.001f, activeScale);
        lockedEditMapRootPosition = mapRoot.transform.position;
        lockedEditMapRootRotation = mapRoot.transform.rotation;
        lockedEditMapWorldToLocalMatrix = mapRoot.transform.worldToLocalMatrix;
        CaptureTrajectoryRootPose(out lockedEditTrajectoryLocalToWorldMatrix, out lockedEditTrajectoryRootRotation);
        editBasisLocked = true;
    }

    public void EndEdit()
    {
        editBasisLocked = false;
        lockedEditPointIndex = -1;
    }

    public bool IsEditBasisLocked()
    {
        return editBasisLocked;
    }

    public bool TryMapProxyPoseToTrajectoryWorld(Vector3 proxyWorldPosition, Quaternion proxyWorldRotation, out Vector3 trajectoryWorldPosition, out Quaternion trajectoryWorldRotation)
    {
        trajectoryWorldPosition = proxyWorldPosition;
        trajectoryWorldRotation = proxyWorldRotation;

        if (mapRoot == null)
        {
            return false;
        }

        if ((!editBasisLocked && activeScale <= 0.0001f) ||
            (editBasisLocked && lockedEditScale <= 0.0001f))
        {
            return false;
        }

        if (editBasisLocked)
        {
            Vector3 lockedProxyLocalPosition = lockedEditMapWorldToLocalMatrix.MultiplyPoint3x4(proxyWorldPosition);
            Vector3 lockedTrajectoryLocalPosition = lockedProxyLocalPosition / lockedEditScale + lockedEditTrajectoryCenterLocal;
            trajectoryWorldPosition = lockedEditTrajectoryLocalToWorldMatrix.MultiplyPoint3x4(lockedTrajectoryLocalPosition);

            Quaternion lockedProxyLocalRotation = Quaternion.Inverse(lockedEditMapRootRotation) * proxyWorldRotation;
            trajectoryWorldRotation = lockedEditTrajectoryRootRotation * lockedProxyLocalRotation;
            return true;
        }

        Vector3 proxyLocalPosition = mapRoot.transform.InverseTransformPoint(proxyWorldPosition);
        Vector3 trajectoryLocalPosition = proxyLocalPosition / activeScale + trajectoryCenterLocal;

        Transform trajectoryRoot = GetTrajectoryRoot();

        if (trajectoryRoot != null)
        {
            trajectoryWorldPosition = trajectoryRoot.TransformPoint(trajectoryLocalPosition);
        }
        else if (Calibration.instance != null)
        {
            trajectoryWorldPosition = Calibration.instance.transform.TransformPoint(trajectoryLocalPosition);
        }
        else
        {
            trajectoryWorldPosition = trajectoryLocalPosition;
        }

        Quaternion proxyLocalRotation = Quaternion.Inverse(mapRoot.transform.rotation) * proxyWorldRotation;
        Quaternion trajectoryRootRotation = trajectoryRoot != null ? trajectoryRoot.rotation : Quaternion.identity;
        if (trajectoryRoot == null && Calibration.instance != null)
        {
            trajectoryRootRotation = Calibration.instance.transform.rotation;
        }
        trajectoryWorldRotation = trajectoryRootRotation * proxyLocalRotation;

        return true;
    }

    void CaptureTrajectoryRootPose(out Matrix4x4 localToWorldMatrix, out Quaternion rootRotation)
    {
        Transform trajectoryRoot = GetTrajectoryRoot();
        if (trajectoryRoot != null)
        {
            localToWorldMatrix = trajectoryRoot.localToWorldMatrix;
            rootRotation = trajectoryRoot.rotation;
            return;
        }

        if (Calibration.instance != null)
        {
            localToWorldMatrix = Calibration.instance.transform.localToWorldMatrix;
            rootRotation = Calibration.instance.transform.rotation;
            return;
        }

        localToWorldMatrix = Matrix4x4.identity;
        rootRotation = Quaternion.identity;
    }

    Transform GetTrajectoryRoot()
    {
        if (sourceVisualizer != null)
        {
            return sourceVisualizer.GetTrajectoryRootTransform();
        }

        if (Calibration.instance != null)
        {
            return Calibration.instance.transform;
        }

        return null;
    }

    public void SetPointHovered(int pointIndex, bool isHovered)
    {
        if (!HasPoint(pointIndex))
        {
            return;
        }

        if (isHovered)
        {
            if (hoveredPointIndex >= 0 && hoveredPointIndex != pointIndex)
            {
                int previous = hoveredPointIndex;
                hoveredPointIndex = -1;
                ApplyPointVisual(previous);
            }

            hoveredPointIndex = pointIndex;
        }
        else if (hoveredPointIndex == pointIndex)
        {
            hoveredPointIndex = -1;
        }

        ApplyPointVisual(pointIndex);
    }

    public void SetPointSelected(int pointIndex, bool isSelected)
    {
        if (!HasPoint(pointIndex))
        {
            return;
        }

        if (isSelected)
        {
            if (selectedPointIndex >= 0 && selectedPointIndex != pointIndex)
            {
                int previous = selectedPointIndex;
                selectedPointIndex = -1;
                ApplyPointVisual(previous);
            }

            selectedPointIndex = pointIndex;
        }
        else if (selectedPointIndex == pointIndex)
        {
            selectedPointIndex = -1;
        }

        ApplyPointVisual(pointIndex);
    }

    public void ClearAllStates()
    {
        int previousHovered = hoveredPointIndex;
        int previousSelected = selectedPointIndex;

        hoveredPointIndex = -1;
        selectedPointIndex = -1;

        ApplyPointVisual(previousHovered);
        if (previousSelected != previousHovered)
        {
            ApplyPointVisual(previousSelected);
        }
    }

    void OnTrajectoryUpdated(List<Vector3> positions, List<Quaternion> rotations, List<float> gripperWidths)
    {
        UpdateTrajectory(positions, rotations);
    }

    void RefreshFromSourceSnapshot()
    {
        if (sourceVisualizer == null)
        {
            return;
        }

        List<Vector3> positions;
        List<Quaternion> rotations;
        List<float> gripperWidths;
        if (sourceVisualizer.TryGetTrajectorySnapshot(out positions, out rotations, out gripperWidths))
        {
            UpdateTrajectory(positions, rotations);
        }
    }

    void UpdateTrajectory(List<Vector3> positions, List<Quaternion> rotations)
    {
        trajectoryLocalPositions.Clear();
        trajectoryLocalRotations.Clear();
        mapLocalPositions.Clear();

        if (positions == null || positions.Count == 0)
        {
            ClearVisualization();
            return;
        }

        trajectoryLocalPositions.AddRange(positions);
        if (rotations != null)
        {
            trajectoryLocalRotations.AddRange(rotations);
        }

        while (trajectoryLocalRotations.Count < trajectoryLocalPositions.Count)
        {
            trajectoryLocalRotations.Add(Quaternion.identity);
        }

        if (!editBasisLocked)
        {
            trajectoryCenterLocal = CalculateBoundsCenter(trajectoryLocalPositions);
            activeScale = Mathf.Max(0.001f, mapScale);
        }
        else
        {
            activeScale = lockedEditScale;
        }

        Vector3 centerForMap = editBasisLocked ? lockedEditTrajectoryCenterLocal : trajectoryCenterLocal;
        for (int i = 0; i < trajectoryLocalPositions.Count; i++)
        {
            mapLocalPositions.Add((trajectoryLocalPositions[i] - centerForMap) * activeScale);
        }

        RebuildVisualization();
    }

    Vector3 CalculateBoundsCenter(List<Vector3> positions)
    {
        Vector3 min = positions[0];
        Vector3 max = positions[0];

        for (int i = 1; i < positions.Count; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }

        return (min + max) * 0.5f;
    }

    void RebuildVisualization()
    {
        EnsureRoot();
        ClearVisualization();

        for (int i = 0; i < mapLocalPositions.Count; i++)
        {
            GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = "MagnifiedChunkPoint_" + i;
            point.transform.SetParent(mapRoot.transform, false);
            point.transform.localPosition = mapLocalPositions[i];
            point.transform.localRotation = Quaternion.identity;
            point.transform.localScale = Vector3.one * pointSize;

            Collider collider = point.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = point.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = pointMaterial;
            }

            pointObjects.Add(point);
            ApplyPointVisual(i);
        }

        for (int i = 0; i < mapLocalPositions.Count - 1; i++)
        {
            GameObject line = CreateLine(mapLocalPositions[i], mapLocalPositions[i + 1], "MagnifiedChunkLine_" + i, lineMaterial, lineWidth);
            if (line != null)
            {
                lineObjects.Add(line);
            }
        }

        if (showAxes)
        {
            CreateAxes();
        }

        SetVisible(visible);
    }

    void CreateAxes()
    {
        for (int i = 0; i < mapLocalPositions.Count && i < trajectoryLocalRotations.Count; i++)
        {
            Vector3 origin = mapLocalPositions[i];
            Quaternion rotation = trajectoryLocalRotations[i];

            AddAxisLine(origin, origin + rotation * Vector3.right * axisLength, "MagnifiedXAxis_" + i, xAxisMaterial);
            AddAxisLine(origin, origin + rotation * Vector3.up * axisLength, "MagnifiedYAxis_" + i, yAxisMaterial);
            AddAxisLine(origin, origin + rotation * Vector3.forward * axisLength, "MagnifiedZAxis_" + i, zAxisMaterial);
        }
    }

    void AddAxisLine(Vector3 start, Vector3 end, string name, Material material)
    {
        GameObject axis = CreateLine(start, end, name, material, lineWidth * 0.7f);
        if (axis != null)
        {
            axisObjects.Add(axis);
        }
    }

    GameObject CreateLine(Vector3 start, Vector3 end, string name, Material material, float width)
    {
        Vector3 direction = end - start;
        float length = direction.magnitude;
        if (length <= 0.0001f)
        {
            return null;
        }

        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        line.name = name;
        line.transform.SetParent(mapRoot.transform, false);
        line.transform.localPosition = (start + end) * 0.5f;
        line.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        line.transform.localScale = new Vector3(width * 2f, length * 0.5f, width * 2f);

        Collider collider = line.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        Renderer renderer = line.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = material;
        }

        return line;
    }

    void ClearVisualization()
    {
        for (int i = 0; i < pointObjects.Count; i++)
        {
            if (pointObjects[i] != null)
            {
                Destroy(pointObjects[i]);
            }
        }
        pointObjects.Clear();

        for (int i = 0; i < lineObjects.Count; i++)
        {
            if (lineObjects[i] != null)
            {
                Destroy(lineObjects[i]);
            }
        }
        lineObjects.Clear();

        for (int i = 0; i < axisObjects.Count; i++)
        {
            if (axisObjects[i] != null)
            {
                Destroy(axisObjects[i]);
            }
        }
        axisObjects.Clear();
    }

    void EnsureHeadTransform()
    {
        if (headTransform != null)
        {
            return;
        }

        if (Camera.main != null)
        {
            headTransform = Camera.main.transform;
        }
    }

    void EnsureRoot()
    {
        if (mapRoot != null)
        {
            return;
        }

        mapRoot = new GameObject("MagnifiedTrajectoryMap");
        ApplyRootPose();
        mapRoot.SetActive(visible);
    }

    void ApplyRootPose()
    {
        if (mapRoot == null)
        {
            return;
        }

        if (mapRoot.transform.parent != null)
        {
            mapRoot.transform.SetParent(null, true);
        }

        if (editBasisLocked)
        {
            mapRoot.transform.position = lockedEditMapRootPosition;
            mapRoot.transform.rotation = lockedEditMapRootRotation;
            mapRoot.transform.localScale = Vector3.one;
            return;
        }

        if (!hasLockedWorldPosition)
        {
            if (headTransform != null)
            {
                lockedWorldPosition = headTransform.TransformPoint(localOffset);
            }
            else
            {
                lockedWorldPosition = localOffset;
            }
        }

        mapRoot.transform.position = lockedWorldPosition;
        mapRoot.transform.rotation = GetTrajectoryRootRotation();
        mapRoot.transform.localScale = Vector3.one;
    }

    Quaternion GetTrajectoryRootRotation()
    {
        Transform trajectoryRoot = GetTrajectoryRoot();
        if (trajectoryRoot != null)
        {
            return trajectoryRoot.rotation;
        }

        if (Calibration.instance != null)
        {
            return Calibration.instance.transform.rotation;
        }

        return Quaternion.identity;
    }

    void CreateMaterials()
    {
        if (pointMaterial != null)
        {
            return;
        }

        pointMaterial = CreateMaterial(pointColor);
        hoverMaterial = CreateMaterial(hoverColor);
        selectedMaterial = CreateMaterial(selectedColor);
        lineMaterial = CreateMaterial(lineColor);
        xAxisMaterial = CreateMaterial(xAxisColor);
        yAxisMaterial = CreateMaterial(yAxisColor);
        zAxisMaterial = CreateMaterial(zAxisColor);
    }

    Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    bool HasPoint(int pointIndex)
    {
        return pointIndex >= 0 && pointIndex < pointObjects.Count;
    }

    void ApplyPointVisual(int pointIndex)
    {
        if (!HasPoint(pointIndex))
        {
            return;
        }

        Renderer renderer = pointObjects[pointIndex].GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        if (pointIndex == selectedPointIndex)
        {
            renderer.material = selectedMaterial;
        }
        else if (pointIndex == hoveredPointIndex)
        {
            renderer.material = hoverMaterial;
        }
        else
        {
            renderer.material = pointMaterial;
        }
    }
}
