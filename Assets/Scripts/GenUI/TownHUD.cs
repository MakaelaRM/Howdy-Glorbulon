using UnityEngine;
using TMPro;

/// Screen-space HUD shown while walking around town
/// Create it with Tools > HUD > Create Town HUD, which builds the layout and fills in these fields.
[RequireComponent(typeof(CanvasGroup))]
public class TownHUD : MonoBehaviour
{
    [Header("Reputation Bar (top right)")]
    [Tooltip("The bar's fill. Its width is driven by its right anchor, so no special sprite is needed.")]
    [SerializeField] private RectTransform reputationFill;
    [SerializeField] private TextMeshProUGUI reputationLevelText;

    [Header("Animation")]
    [Tooltip("How fast the HUD fades in and out (alpha per second).")]
    [SerializeField] private float fadeSpeed = 6f;
    [Tooltip("Seconds to wait after the HUD fades back in before changes start animating.")]
    [SerializeField] private float revealDelay = 0.5f;
    [Tooltip("How fast the reputation bar fills (full bars per second).")]
    [SerializeField] private float fillSpeed = 0.6f;
    [Tooltip("How big a number pops when it changes.")]
    [SerializeField] private float punchScale = 1.35f;
    [SerializeField] private float punchDuration = 0.55f;

    [Header("Popups")]
    [Tooltip("Text under the bar showing +/- rep. Created automatically if left empty.")]
    [SerializeField] private TextMeshProUGUI reputationPopupText;
    [SerializeField] private Color reputationGainColor = new Color(0.4f, 1f, 0.45f);
    [SerializeField] private Color reputationLossColor = new Color(1f, 0.35f, 0.3f);
    [Tooltip("How long the popup stays on screen, including its fade out.")]
    [SerializeField] private float popupDuration = 1.8f;
    [Tooltip("How far the popup drifts down while it's showing (pixels).")]
    [SerializeField] private float popupDrift = 14f;

    private CanvasGroup group;
    private PlayerInteract player;
    private PlayerStats stats;

    private int shownLevel, shownTotalRep;
    private float shownFill;
    private bool synced;
    private float visibleTime;

    private Punch levelPunch;
    private Popup repPopup;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        levelPunch = new Punch(reputationLevelText);

        if (reputationPopupText == null) reputationPopupText = CreateRepPopupText();

        repPopup = new Popup(reputationPopupText, " REP");
    }

    void Start()
    {
        player = FindFirstObjectByType<PlayerInteract>();
        stats = PlayerStats.Instance;
        if (stats == null)
            Debug.LogWarning("TownHUD can't find a PlayerStats. Add one to your Player.", this);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Hide while in interact mode
        bool visible = player == null || !player.IsInteracting;
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, fadeSpeed * dt);

        if (stats == null)
        {
            stats = PlayerStats.Instance;
            if (stats == null) return;
        }

        if (!synced)
        {
            SnapToStats();
            synced = true;
        }

        if (visible && group.alpha >= 0.99f) visibleTime += dt;
        else visibleTime = 0f;

        if (visibleTime >= revealDelay)
        {
            UpdateReputation(dt);
            CheckReputationPopup();
        }

        levelPunch.Tick(dt, punchDuration, punchScale);
        repPopup.Tick(dt, popupDuration, popupDrift);
    }

    private void SnapToStats()
    {
        shownLevel = stats.ReputationLevel;
        shownFill = stats.ReputationProgress;
        shownTotalRep = stats.TotalReputation;

        SetText(reputationLevelText, shownLevel);
        SetFill(shownFill);
    }

    private void UpdateReputation(float dt)
    {
        int targetLevel = stats.ReputationLevel;
        float targetFill = stats.ReputationProgress;
        float step = fillSpeed * dt;

        if (shownLevel < targetLevel)
        {
            shownFill = Mathf.MoveTowards(shownFill, 1f, step);
            if (shownFill >= 1f)
            {
                shownLevel++;
                shownFill = 0f;
                SetText(reputationLevelText, shownLevel);
                levelPunch.Play();
            }
        }
        else if (shownLevel > targetLevel)
        {
            shownLevel = targetLevel;
            shownFill = targetFill;
            SetText(reputationLevelText, shownLevel);
        }
        else
        {
            shownFill = Mathf.MoveTowards(shownFill, targetFill, step);
        }

        SetFill(shownFill);
    }

    private void SetFill(float amount)
    {
        if (reputationFill == null) return;
        reputationFill.anchorMin = Vector2.zero;
        reputationFill.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        reputationFill.offsetMin = Vector2.zero;
        reputationFill.offsetMax = Vector2.zero;
    }

    private static void SetText(TextMeshProUGUI text, int value)
    {
        if (text != null) text.text = value.ToString();
    }

    private void CheckReputationPopup()
    {
        int total = stats.TotalReputation;
        if (total == shownTotalRep) return;

        int delta = total - shownTotalRep;
        shownTotalRep = total;
        repPopup.Show(delta, popupDuration, reputationGainColor, reputationLossColor);
    }

    // Builds the popup text under the bar if one wasn't assigned in the Inspector
    private TextMeshProUGUI CreateRepPopupText()
    {
        if (reputationFill == null) return null;

        Transform bar = reputationFill;
        while (bar != null && bar.name != "Reputation") bar = bar.parent;
        if (bar == null) bar = reputationFill.parent;

        var go = new GameObject("ReputationPopup", typeof(RectTransform));
        go.transform.SetParent(bar, false);

        var text = go.AddComponent<TextMeshProUGUI>();
        if (reputationLevelText != null) text.font = reputationLevelText.font;
        text.fontSize = 28f;
        text.alignment = TextAlignmentOptions.Right;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-4f, -8f);
        rect.sizeDelta = new Vector2(220f, 36f);
        return text;
    }

    // Briefly scales a number up and eases it back down
    private class Punch
    {
        private readonly TextMeshProUGUI text;
        private float time = float.MaxValue;

        public Punch(TextMeshProUGUI text)
        {
            this.text = text;
        }

        public void Play()
        {
            time = 0f;
        }

        public void Tick(float dt, float duration, float scale)
        {
            if (text == null || time >= duration) return;

            time += dt;
            float k = Mathf.Clamp01(time / duration);
            float easeOut = 1f - (1f - k) * (1f - k);
            text.transform.localScale = Vector3.one * Mathf.Lerp(scale, 1f, easeOut);
        }
    }

    private class Popup
    {
        private readonly TextMeshProUGUI text;
        private readonly string suffix;
        private readonly Vector2 home;
        private int amount;
        private float time = float.MaxValue;

        public Popup(TextMeshProUGUI text, string suffix)
        {
            this.text = text;
            this.suffix = suffix;
            if (text != null)
            {
                home = text.rectTransform.anchoredPosition;
                text.alpha = 0f;
            }
        }

        public void Show(int delta, float duration, Color gain, Color loss)
        {
            if (text == null || delta == 0) return;

            bool active = time < duration;
            if (active && (delta > 0) == (amount > 0)) amount += delta;
            else amount = delta;

            text.text = (amount > 0 ? "+" : "") + amount + suffix;
            text.color = amount > 0 ? gain : loss;
            text.alpha = 1f;
            time = 0f;
        }

        public void Tick(float dt, float duration, float drift)
        {
            if (text == null || time >= duration) return;

            time += dt;
            float s = time;

            float scale = s < 0.12f ? Mathf.Lerp(0.6f, 1.15f, s / 0.12f)
                        : s < 0.24f ? Mathf.Lerp(1.15f, 1f, (s - 0.12f) / 0.12f)
                        : 1f;
            float fadeStart = duration * 0.6f;
            float alpha = s < fadeStart ? 1f : 1f - Mathf.InverseLerp(fadeStart, duration, s);

            text.transform.localScale = Vector3.one * scale;
            text.alpha = alpha;
            text.rectTransform.anchoredPosition = home + Vector2.down * (drift * Mathf.Clamp01(s / duration));
        }
    }
}
