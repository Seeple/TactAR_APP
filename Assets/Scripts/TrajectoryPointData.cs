using UnityEngine;

/// <summary>
/// 存储轨迹点的数据和状态
/// 附加到每个轨迹点GameObject上，用于射线选择和交互
/// </summary>
public class TrajectoryPointData : MonoBehaviour
{
    public int pointIndex;  // 点在轨迹中的索引
    public ChunkVisualizer visualizer;  // 引用父Visualizer
    public bool isSelected = false;
    public bool isHovered = false;
    
    private Renderer pointRenderer;
    private Color originalColor;
    
    void Awake()
    {
        pointRenderer = GetComponent<Renderer>();
        if (pointRenderer != null && pointRenderer.material != null)
        {
            originalColor = pointRenderer.material.color;
        }
    }
    
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateVisual();
    }
    
    public void SetHovered(bool hovered)
    {
        isHovered = hovered;
        UpdateVisual();
    }
    
    void UpdateVisual()
    {
        if (pointRenderer == null || pointRenderer.material == null) return;
        
        if (isSelected && visualizer != null)
        {
            pointRenderer.material.color = visualizer.selectedPointColor;
        }
        else if (isHovered && visualizer != null)
        {
            pointRenderer.material.color = visualizer.hoverPointColor;
        }
        else
        {
            pointRenderer.material.color = originalColor;
        }
    }
}
