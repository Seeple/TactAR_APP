using UnityEngine;

public class MyKeyboard : MonoBehaviour
{
    public TMPro.TextMeshProUGUI text;
    public TMPro.TextMeshProUGUI showText;
    public VRController Client;

    public float indexTipJumpThreshold = 0.1f;
    public float editedPointJumpThreshold = 0.1f;
    public float rotationJumpThresholdDeg = 30f;

    private Vector3 lastIndexTipPos;
    private Quaternion lastIndexTipRot = Quaternion.identity;
    private Vector3 lastEditedPos;
    private Quaternion lastEditedRot = Quaternion.identity;
    private bool hasLastSample = false;

    void Start()
    {
        UpdateHeaderText(Vector3.zero, Quaternion.identity, Vector3.zero, Quaternion.identity, false, -1, false);
    }

    void Update()
    {
        if (showText == null || Client == null)
        {
            return;
        }

        bool hasIndex = Client.TryGetIndexTipPose(out Vector3 indexPos, out Quaternion indexRot);
        bool hasEdit = Client.TryGetEditedPointPose(out Vector3 editPos, out Quaternion editRot, out bool isEditing, out int selectedIndex);

        bool isJump = false;
        if (hasLastSample && hasIndex)
        {
            if (Vector3.Distance(indexPos, lastIndexTipPos) > indexTipJumpThreshold)
            {
                isJump = true;
            }
            if (Quaternion.Angle(indexRot, lastIndexTipRot) > rotationJumpThresholdDeg)
            {
                isJump = true;
            }
        }

        if (hasLastSample && hasEdit && isEditing)
        {
            if (Vector3.Distance(editPos, lastEditedPos) > editedPointJumpThreshold)
            {
                isJump = true;
            }
            if (Quaternion.Angle(editRot, lastEditedRot) > rotationJumpThresholdDeg)
            {
                isJump = true;
            }
        }

        UpdateHeaderText(indexPos, indexRot, editPos, editRot, isEditing, selectedIndex, isJump);

        if (hasIndex)
        {
            lastIndexTipPos = indexPos;
            lastIndexTipRot = indexRot;
        }
        if (hasEdit && isEditing)
        {
            lastEditedPos = editPos;
            lastEditedRot = editRot;
        }
        hasLastSample = hasIndex || (hasEdit && isEditing);
    }

    void UpdateHeaderText(Vector3 indexPos, Quaternion indexRot, Vector3 editPos, Quaternion editRot, bool isEditing, int selectedIndex, bool isJump)
    {
        if (showText == null)
        {
            return;
        }

        showText.color = isJump ? Color.red : Color.white;

        string header = "Press B to toggle magnified-map selection\n" +
                        "Press Y to lock the last action chunk point";

        string indexLine = $"IndexTip: {indexPos.x:F3}, {indexPos.y:F3}, {indexPos.z:F3}";
        string editLine = isEditing
            ? $"EditedPoint: {editPos.x:F3}, {editPos.y:F3}, {editPos.z:F3} | idx: {selectedIndex} | editing: {isEditing}"
            : "EditedPoint: N/A | idx: -1 | editing: false";

        showText.text = header + "\n" + indexLine + "\n" + editLine;
    }
    
    public void Add(string s)
    {
        text.text = text.text += s;
    }

    public void Remove()
    {
        text.text = text.text.Remove(text.text.Length - 1);
    }

    public void RefreshIP()
    {
        Client.RefreshIP(text.text);
    }

    public void SwitchLRController()
    {
        Client.LRinverse = !Client.LRinverse;
    }
}
