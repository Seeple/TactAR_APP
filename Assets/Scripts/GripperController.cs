using UnityEngine;

public class GripperJawController : MonoBehaviour
{
    public Transform jawLeft;
    public Transform jawRight;
    public float jawWidth = 0.085f;
    public float robotClosedWidth = 0.0f;
    public float robotOpenWidth = 0.085f;
    public float leftClosedLocalX = 0.08f;
    public float leftOpenLocalX = 0.03f;
    public float rightClosedLocalX = -0.08f;
    public float rightOpenLocalX = -0.03f;

    void Update()
    {
        ApplyJawWidth();
    }

    void ApplyJawWidth()
    {
        float normalizedWidth = Mathf.Clamp01(Mathf.InverseLerp(robotClosedWidth, robotOpenWidth, jawWidth));

        if (jawLeft != null)
        {
            jawLeft.localPosition = new Vector3(
                Mathf.Lerp(leftClosedLocalX, leftOpenLocalX, normalizedWidth),
                jawLeft.localPosition.y,
                jawLeft.localPosition.z
            );
        }

        if (jawRight != null)
        {
            jawRight.localPosition = new Vector3(
                Mathf.Lerp(rightClosedLocalX, rightOpenLocalX, normalizedWidth),
                jawRight.localPosition.y,
                jawRight.localPosition.z
            );
        }
    }
}
