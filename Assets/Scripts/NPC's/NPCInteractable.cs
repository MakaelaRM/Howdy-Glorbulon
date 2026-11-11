using System.Collections.Generic;
using UnityEngine;

/// An NPC the player can talk to, with optional rewards, return-visit dialogue and a shop.
public class NPCInteractable : MonoBehaviour
{
    [Header("Name Prompt")]
    [SerializeField] private string npcName;
    [SerializeField] private GameObject chatBubblePrefab;
    [Tooltip("Where the tip of the name bubble's tail sits, relative to the top of the head. X follows the camera's right, Y is up.")]
    [SerializeField] private Vector2 promptAnchorOffset = new Vector2(0f, 0.12f);
    [Tooltip("How far the name bubble leans back toward the camera. 0 = upright, 1 = faces the camera fully.")]
    [SerializeField, Range(0f, 1f)] private float promptTilt = 0.5f;

    [Header("Camera Focus")]
    [SerializeField, Range(0f, 1f)] private float focusHeight = 0.75f;

    [Header("Dialogue")]
    [SerializeField, TextArea(2, 5)]
    private string dialogueText = "You're not from around these parts, are you Partner? I'd advise you keep moving.";
    [SerializeField] private GameObject dialogueBubblePrefab;
    [Tooltip("Where the tip of the dialogue bubble's tail sits, relative to the top of the head. X follows the camera's right (negative = left), Y is up.")]
    [SerializeField] private Vector2 dialogueAnchorOffset = new Vector2(-0.2f, 0.06f);
    [SerializeField] private float dialogueScale = 1f;
    [Tooltip("Shrinks the dialogue bubble if it would run off the edge of the screen.")]
    [SerializeField] private bool fitDialogueToView = true;
    [Tooltip("Gap to keep between the bubble and the screen edge, as a fraction of the screen.")]
    [SerializeField, Range(0f, 0.2f)] private float viewMargin = 0.04f;

    [Header("Audio Settings")]
    [SerializeField] public AudioClip dialogueAudio;
    private AudioSource audioSource;
    

    [Header("Rewards (optional)")]
    [Tooltip("Given when the player interacts. 0 = nothing. Negative takes away (e.g. paying for a meal).")]
    [SerializeField] private int moneyReward;
    [SerializeField] private int foodReward;
    [SerializeField] private int drinksReward;
    [SerializeField] private int reputationReward;
    [Tooltip("Only give the rewards the first time the player talks to this NPC.")]
    [SerializeField] private bool rewardOnlyOnce = true;

    [Header("Return Visit (optional)")]
    [SerializeField, TextArea(2, 5)] private string returnDialogueText;
    [SerializeField] private int returnMoneyReward;
    [SerializeField] private int returnFoodReward;
    [SerializeField] private int returnDrinksReward;
    [SerializeField] private int returnReputationReward;
    [SerializeField] private bool returnRewardOnlyOnce = true;

    [Header("Shop (optional)")]
    [SerializeField] private bool hasShop;
    [Tooltip("Reputation level the player needs before the shop option appears.")]
    [SerializeField, Min(0)] private int shopMinReputation = 1;
    [Tooltip("Shown at the top of the shop menu. Leave empty for \"<NPC Name>'s Shop\".")]
    [SerializeField] private string shopTitle;
    [Tooltip("Up to 4 items fit on a page; more items add extra pages.")]
    [SerializeField] private List<ShopItem> shopItems = new List<ShopItem>();

    [Header("Hide While Interacting (optional)")]
    [SerializeField] private Renderer[] hideWhileInteracting;
    
    

    private readonly List<Behaviour> pausedWhileInteracting = new List<Behaviour>();

    private bool rewardGiven;
    private bool returnRewardGiven;
    private bool hasVisited;
    private bool onReturnVisit;

    private SpeechBubble3D promptBubble;
    private SpeechBubble3D dialogueBubble;
    private float promptBaseScale = 1f;
    private float dialogueBaseScale = 1f;
    private float modelHeight;

    public Vector3 FocusPoint => transform.position + Vector3.up * (modelHeight * focusHeight);

    public bool CanOpenShop =>
        hasShop && PlayerStats.Instance != null && PlayerStats.Instance.ReputationLevel >= shopMinReputation;

    public string ShopTitle => string.IsNullOrEmpty(shopTitle) ? npcName + "'s Shop" : shopTitle;

    public IReadOnlyList<ShopItem> ShopItems => shopItems;

    private Vector3 HeadTop => transform.position + Vector3.up * modelHeight;

    void Reset()
    {
        #if UNITY_EDITOR
            chatBubblePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/ChatBubble.prefab");
            dialogueBubblePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/DialogueBubble.prefab");
        #endif
    }

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (string.IsNullOrEmpty(npcName))
            npcName = gameObject.name;
        modelHeight = GetTopOfModel() - transform.position.y;

        promptBubble = SpawnBubble(chatBubblePrefab, "Chat Bubble Prefab", out promptBaseScale);
        dialogueBubble = SpawnBubble(dialogueBubblePrefab, "Dialogue Bubble Prefab", out dialogueBaseScale);
    }

    private SpeechBubble3D SpawnBubble(GameObject prefab, string fieldName, out float baseScale)
    {
        baseScale = 1f;
        if (prefab == null)
        {
            Debug.LogWarning(name + " has no " + fieldName + " assigned.", this);
            return null;
        }

        GameObject instance = Instantiate(prefab, transform);
        instance.name = prefab.name;
        if (!instance.TryGetComponent(out SpeechBubble3D bubble))
        {
            Debug.LogError(prefab.name + " isn't a 3D speech bubble yet. Run Tools > Speech Bubbles > Rebuild Bubble Prefabs.", prefab);
            Destroy(instance);
            return null;
        }

        baseScale = prefab.transform.localScale.x;
        bubble.HideImmediate();
        return bubble;
    }

    private float GetTopOfModel()
    {
        float top = transform.position.y;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            top = Mathf.Max(top, r.bounds.max.y);
        return top;
    }

    public void ShowPrompt(string keyName, string shopKeyName = null)
    {
        if (promptBubble == null) return;

        string prompt = "Press " + promptBubble.HighlightKey(keyName) + " to interact";
        if (CanOpenShop && !string.IsNullOrEmpty(shopKeyName))
            prompt += ", " + promptBubble.HighlightKey(shopKeyName) + " to shop";
        else if (hasShop)
            prompt += ", can shop at " + promptBubble.HighlightLevel(shopMinReputation);
        promptBubble.SetContent(npcName, prompt);

        Transform view = Camera.main != null ? Camera.main.transform : null;
        GetViewAxes(view, out Vector3 right, out Vector3 flatForward);

        Vector3 facing = flatForward;
        if (view != null) facing = Vector3.Slerp(flatForward, view.forward, promptTilt).normalized;

        Vector3 anchor = HeadTop + right * promptAnchorOffset.x + Vector3.up * promptAnchorOffset.y;
        PlaceBubble(promptBubble, anchor, Quaternion.LookRotation(facing, Vector3.up), promptBaseScale);
        promptBubble.Show();
    }

    public void HidePrompt()
    {
        if (promptBubble != null) promptBubble.Hide();
    }

    public void ShowDialogue(string exitKeyName, Transform viewer)
    {
        if (dialogueBubble == null) return;

        string text = onReturnVisit && !string.IsNullOrEmpty(returnDialogueText) ? returnDialogueText : dialogueText;
        dialogueBubble.SetContent(text, "Press " + dialogueBubble.HighlightKey(exitKeyName) + " to escape");

        GetViewAxes(viewer, out Vector3 right, out Vector3 flatForward);
        Vector3 anchor = HeadTop + right * dialogueAnchorOffset.x + Vector3.up * dialogueAnchorOffset.y;
        Quaternion rotation = Quaternion.LookRotation(flatForward, Vector3.up);

        float scale = dialogueBaseScale * dialogueScale;
        if (fitDialogueToView && viewer != null && viewer.TryGetComponent(out Camera cam))
            scale = FitScaleToView(cam, dialogueBubble.LocalBounds, anchor, rotation, scale);

        PlaceBubble(dialogueBubble, anchor, rotation, scale);
        dialogueBubble.Show();
    }

    public void HideDialogue()
    {
        if (dialogueBubble != null) dialogueBubble.Hide();
        SetHiddenRenderers(true);
        SetPaused(false);
    }

    public void Interact()
    {
        Debug.Log("Interacting with: " + npcName);
        SetPaused(true);
        onReturnVisit = rewardOnlyOnce && hasVisited;
        hasVisited = true;
        SetHiddenRenderers(false);
        playSoundOnInteract();
        if (onReturnVisit)
        {
            if (!(returnRewardOnlyOnce && returnRewardGiven) &&
                GiveRewards(returnMoneyReward, returnFoodReward, returnDrinksReward, returnReputationReward))
                returnRewardGiven = true;
        }
        else if (!(rewardOnlyOnce && rewardGiven) &&
                 GiveRewards(moneyReward, foodReward, drinksReward, reputationReward))
        {
            rewardGiven = true;
        }
    }

    public void EnterShop()
    {
        Debug.Log("Opening shop: " + npcName);
        SetPaused(true);
        SetHiddenRenderers(false);
    }

    public void ExitShop()
    {
        SetHiddenRenderers(true);
        SetPaused(false);
    }

    private void SetPaused(bool paused)
    {
        if (!paused)
        {
            foreach (Behaviour b in pausedWhileInteracting)
                if (b != null) b.enabled = true;
            pausedWhileInteracting.Clear();
            return;
        }

        if (pausedWhileInteracting.Count > 0) return;

        foreach (Behaviour b in GetComponentsInChildren<Behaviour>())
        {
            if (!b.enabled || !(b is NpcWalker || b is NpcProceduralAnim || b is AlienBlink || b is Animator)) continue;
            if (b.GetComponentInParent<SpeechBubble3D>() != null) continue;

            if (b is NpcProceduralAnim anim) anim.ResetPose();
            b.enabled = false;
            pausedWhileInteracting.Add(b);
        }
    }
        
    public void playSoundOnInteract()
    {
        audioSource.PlayOneShot(dialogueAudio);
    }

    private void SetHiddenRenderers(bool visible)
    {
        if (hideWhileInteracting == null) return;
        foreach (Renderer r in hideWhileInteracting)
            if (r != null) r.enabled = visible;
    }

    private bool GiveRewards(int money, int food, int drinks, int reputation)
    {
        if (money == 0 && food == 0 && drinks == 0 && reputation == 0) return false;

        PlayerStats stats = PlayerStats.Instance;
        if (stats == null)
        {
            Debug.LogWarning(npcName + " has rewards but there's no PlayerStats in the scene.", this);
            return false;
        }

        stats.AddReputation(reputation);

        if (money != 0 || food != 0 || drinks != 0)
        {
            Inventory inventory = Inventory.Find();
            if (inventory == null)
            {
                Debug.LogWarning(npcName + " gives currency, food or drinks but there's no Inventory in the scene.", this);
                stats.AddMoney(money);
            }
            else
            {
                inventory.AddCurrency(money);
                inventory.AddFood(food);
                inventory.AddDrinks(drinks);
            }
        }
        return true;
    }

    private static void GetViewAxes(Transform view, out Vector3 right, out Vector3 flatForward)
    {
        right = Vector3.right;
        flatForward = Vector3.forward;
        if (view == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(view.up, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) return;

        flatForward = forward.normalized;
        right = Vector3.Cross(Vector3.up, flatForward);
    }

    private static void PlaceBubble(SpeechBubble3D bubble, Vector3 position, Quaternion rotation, float worldScale)
    {
        Transform t = bubble.transform;
        t.SetPositionAndRotation(position, rotation);

        Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(
            worldScale / NonZero(parentScale.x),
            worldScale / NonZero(parentScale.y),
            worldScale / NonZero(parentScale.z));
    }

    private static float NonZero(float value) => Mathf.Abs(value) < 1e-5f ? 1f : value;

    private float FitScaleToView(Camera cam, Rect bounds, Vector3 anchor, Quaternion rotation, float desired)
    {
        if (!PointInView(cam, anchor, 0f)) return desired;
        if (BoundsInView(cam, bounds, anchor, rotation, desired)) return desired;

        float low = desired * 0.5f;
        float high = desired;
        for (int i = 0; i < 12; i++)
        {
            float mid = (low + high) * 0.5f;
            if (BoundsInView(cam, bounds, anchor, rotation, mid)) low = mid;
            else high = mid;
        }
        return low;
    }

    private bool BoundsInView(Camera cam, Rect bounds, Vector3 anchor, Quaternion rotation, float scale)
    {
        return PointInView(cam, anchor + rotation * new Vector3(bounds.xMin * scale, bounds.yMin * scale, 0f), viewMargin)
            && PointInView(cam, anchor + rotation * new Vector3(bounds.xMax * scale, bounds.yMin * scale, 0f), viewMargin)
            && PointInView(cam, anchor + rotation * new Vector3(bounds.xMin * scale, bounds.yMax * scale, 0f), viewMargin)
            && PointInView(cam, anchor + rotation * new Vector3(bounds.xMax * scale, bounds.yMax * scale, 0f), viewMargin);
    }

    private static bool PointInView(Camera cam, Vector3 worldPoint, float margin)
    {
        Vector3 v = cam.WorldToViewportPoint(worldPoint);
        return v.z > 0f && v.x >= margin && v.x <= 1f - margin && v.y >= margin && v.y <= 1f - margin;
    }
}