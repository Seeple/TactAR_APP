using UnityEngine;

public class GripperJawController : MonoBehaviour
{
    public Transform jawLeft;
    public Transform jawRight;
    public float jawWidth = 0.085f; // total opening width

    void Update()
    {
        float half = jawWidth * 0.5f;
        if (jawLeft != null) jawLeft.localPosition = new Vector3(-half, jawLeft.localPosition.y, jawLeft.localPosition.z);
        if (jawRight != null) jawRight.localPosition = new Vector3(half, jawRight.localPosition.y, jawRight.localPosition.z);
    }
}
