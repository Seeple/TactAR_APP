using UnityEngine;

public class GripperJawController : MonoBehaviour
{
    public Transform jawLeft;
    public Transform jawRight;
    public float jawWidth = 0.085f; // total opening width
    public bool jawWidthInWorldSpace = true;

    void Update()
    {
        float width = Mathf.Max(0f, jawWidth);
        if (jawWidthInWorldSpace)
        {
            float scaleX = Mathf.Abs(transform.lossyScale.x);
            if (scaleX > 1e-6f)
            {
                width /= scaleX;
            }
        }

        float half = width * 0.5f;
        if (jawLeft != null) jawLeft.localPosition = new Vector3(-half, jawLeft.localPosition.y, jawLeft.localPosition.z);
        if (jawRight != null) jawRight.localPosition = new Vector3(half, jawRight.localPosition.y, jawRight.localPosition.z);
    }
}
