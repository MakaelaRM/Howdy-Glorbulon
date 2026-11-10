
using UnityEngine;

public class Item : MonoBehaviour
{
    public ItemSO item;
    public int amount = 1;

    [Header("Pickup Prompt")]
    [SerializeField] private GameObject chatBubblePrefab;
    [SerializeField] private Vector2 promptOffset = new Vector2(0f, 0.12f);
    [SerializeField, Range(0f, 1f)] private float promptTilt = 0.5f;

    private SpeechBubble3D promptBubble;
    private float promptBaseScale = 1f;
    private float modelHeight;

    [Header("Visual Model")]
    public Transform visualModel;

    private void Awake()
    {
        modelHeight = GetTopOfModel() - transform.position.y;

        if (chatBubblePrefab == null)
        {
            Debug.LogWarning(name + " has no Chat Bubble Prefab assigned.");
            return;
        }

        GameObject instance = Instantiate(chatBubblePrefab, transform);
        instance.name = chatBubblePrefab.name;

        if (!instance.TryGetComponent(out promptBubble))
        {
            Debug.LogError("Chat Bubble Prefab needs SpeechBubble3D!");
            Destroy(instance);
            return;
        }

        promptBaseScale = chatBubblePrefab.transform.localScale.x;
        promptBubble.HideImmediate();
    }

    private float GetTopOfModel()
    {
        float top = transform.position.y;

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            top = Mathf.Max(top, r.bounds.max.y);

        return top;
    }

    public void ShowPickupPrompt()
    {
        if (promptBubble == null)
            return;

        string itemName = item != null ? item.itemName : name;

        string prompt = "Press " +
            promptBubble.HighlightKey("E") +
            " to pick up";

        promptBubble.SetContent(itemName, prompt);

        Transform view = Camera.main != null
            ? Camera.main.transform
            : null;

        Vector3 right = Vector3.right;
        Vector3 flatForward = Vector3.forward;

        if (view != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(
                view.forward, Vector3.up
            );

            if (forward.sqrMagnitude > 0.0001f)
            {
                flatForward = forward.normalized;
                right = Vector3.Cross(Vector3.up, flatForward);
            }
        }

        Vector3 facing = flatForward;

        if (view != null)
            facing = Vector3.Slerp(
                flatForward, view.forward, promptTilt
            ).normalized;

        Vector3 anchor =
            transform.position +
            Vector3.up * modelHeight +
            right * promptOffset.x +
            Vector3.up * promptOffset.y;

        Transform bubbleTransform = promptBubble.transform;

        bubbleTransform.SetPositionAndRotation(
            anchor,
            Quaternion.LookRotation(facing, Vector3.up)
        );

        Vector3 parentScale = bubbleTransform.parent.lossyScale;

        bubbleTransform.localScale = new Vector3(
            promptBaseScale / Mathf.Max(Mathf.Abs(parentScale.x), 0.00001f),
            promptBaseScale / Mathf.Max(Mathf.Abs(parentScale.y), 0.00001f),
            promptBaseScale / Mathf.Max(Mathf.Abs(parentScale.z), 0.00001f)
        );

        promptBubble.Show();
    }

    public void HidePickupPrompt()
    {
        if (promptBubble != null)
            promptBubble.Hide();
    }
}


//using UnityEngine;

//public class Item : MonoBehaviour
//{
//    public ItemSO item;
//    public int amount = 1;
//}
