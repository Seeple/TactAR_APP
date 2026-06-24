using UnityEngine;

public class GripperJawController : MonoBehaviour
{
    public Transform jawLeft;
    public Transform jawRight;
    public float jawWidth = 0.085f; // total opening width
    public float robotClosedWidth = 0.0f;
    public float robotOpenWidth = 0.085f;
    public float closedLocalHalfOpening = 0.0f;
    public float openLocalHalfOpening = -1.0f;

    private bool initialized = false;
    private float jawCenterX = 0.0f;
    private float leftDirection = -1.0f;
    private float rightDirection = 1.0f;
    private float calibratedOpenLocalHalfOpening = 0.0f;

    void Awake()
    {
        InitializeJawCalibration();
    }

    void Update()
    {
        ApplyJawWidth();
    }

    void InitializeJawCalibration()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;

        if (jawLeft == null || jawRight == null)
        {
            calibratedOpenLocalHalfOpening = Mathf.Max(0.0f, openLocalHalfOpening);
            return;
        }

        float leftOpenX = jawLeft.localPosition.x;
        float rightOpenX = jawRight.localPosition.x;
        jawCenterX = (leftOpenX + rightOpenX) * 0.5f;

        leftDirection = Mathf.Sign(leftOpenX - jawCenterX);
        rightDirection = Mathf.Sign(rightOpenX - jawCenterX);
        if (Mathf.Abs(leftDirection) < 1e-6f)
        {
            leftDirection = -1.0f;
        }
        if (Mathf.Abs(rightDirection) < 1e-6f)
        {
            rightDirection = 1.0f;
        }

        float initialHalfOpening = Mathf.Abs(rightOpenX - leftOpenX) * 0.5f;
        calibratedOpenLocalHalfOpening = openLocalHalfOpening >= 0.0f
            ? openLocalHalfOpening
            : initialHalfOpening;
    }

    void ApplyJawWidth()
    {
        InitializeJawCalibration();

        float normalizedWidth = Mathf.InverseLerp(robotClosedWidth, robotOpenWidth, Mathf.Max(0.0f, jawWidth));
        float targetHalfOpening = Mathf.Lerp(
            Mathf.Max(0.0f, closedLocalHalfOpening),
            Mathf.Max(0.0f, calibratedOpenLocalHalfOpening),
            Mathf.Clamp01(normalizedWidth)
        );

        if (jawLeft != null)
        {
            jawLeft.localPosition = new Vector3(
                jawCenterX + leftDirection * targetHalfOpening,
                jawLeft.localPosition.y,
                jawLeft.localPosition.z
            );
        }

        if (jawRight != null)
        {
            jawRight.localPosition = new Vector3(
                jawCenterX + rightDirection * targetHalfOpening,
                jawRight.localPosition.y,
                jawRight.localPosition.z
            );
        }
    }
}
