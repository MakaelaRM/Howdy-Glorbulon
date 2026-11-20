using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// Turns the inventory window (opened with I) into a two-tab window: INVENTORY and QUESTS.
/// The quest log lives inside the same window InventoryStyle draws, so it has the same size, rim,
/// colours and font, and scales with the screen the same way.
///
/// Setup: put it on the same object as your Inventory and InventoryStyle scripts (in every scene).
/// Switch tabs by clicking them at the top of the window or pressing Tab.
public class QuestLogUI : MonoBehaviour
{
    [Tooltip("Leave empty to use InventoryStyle's font (Kleader).")]
    public TMP_FontAsset font;
    [Tooltip("The inventory panel that I opens. Leave empty to use Character Movement's Inventory UI.")]
    public GameObject inventoryPanel;
    public Key switchTabKey = Key.Tab;

    [Header("While the window is open")]
    [Tooltip("Pause the game (enemies, bosses, timers) while the inventory / quest log is open.")]
    public bool pauseGame = true;
    [Tooltip("Switch off weapon swapping and attacking while it's open, so Tab and clicks only work in the window.")]
    public bool blockWeapons = true;
    [Tooltip("Extra scripts to switch off while it's open (optional).")]
    public List<Behaviour> alsoDisable = new List<Behaviour>();

    float timeScaleBefore = 1f;
    bool paused;
    readonly List<Behaviour> disabled = new List<Behaviour>();

    [Header("Text size (relative to the inventory's hint text)")]
    [Range(0.4f, 1.5f)] public float questTitleSize = 0.95f;
    [Range(0.4f, 1.5f)] public float stepTextSize = 0.72f;
    [Tooltip("Size of the quest description and the crossed-off steps.")]
    [Range(0.2f, 1.5f)] public float descriptionSize = 0.45f;

    enum Tab { Inventory, Quests }
    Tab tab = Tab.Inventory;
    bool wasOpen, dirty = true;

    InventoryStyle style;
    RectTransform builtFor;                        // the InventoryStyle window we built into
    RectTransform tabsRoot, logRoot, content;
    Image invTabImg, questTabImg;
    TextMeshProUGUI invTabText, questTabText, logText;

    void Start()
    {
        if (!inventoryPanel)
        {
            CharacterMovement m = FindAnyObjectByType<CharacterMovement>();
            if (m) inventoryPanel = m.inventoryUI;
        }
        style = GetComponent<InventoryStyle>();
        if (!style) style = FindAnyObjectByType<InventoryStyle>();
        if (!inventoryPanel || !style)
        {
            Debug.LogWarning("[QuestLogUI] Needs an inventory panel and InventoryStyle (put it on the object with Inventory + InventoryStyle).", this);
            enabled = false;
            return;
        }
        style.Applied += OnStyleApplied;
        QuestManager.Changed += MarkDirty;
        if (Font) QuestManager.UIFont = Font;
    }

    void OnDestroy()
    {
        if (style) style.Applied -= OnStyleApplied;
        QuestManager.Changed -= MarkDirty;
        SetGameplayPaused(false);   // never leave the game frozen (e.g. changing scene with the window open)
    }

    void OnDisable() { SetGameplayPaused(false); wasOpen = false; }

    // Pauses time and switches off weapon swapping / attacks while the window is open.
    void SetGameplayPaused(bool on)
    {
        if (on)
        {
            if (pauseGame && !paused)
            {
                timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                paused = true;
            }
            if (blockWeapons && disabled.Count == 0)
            {
                var list = new List<Behaviour>();
                list.AddRange(FindObjectsByType<WeaponSwap>());
                list.AddRange(FindObjectsByType<BlasterAttack>());
                list.AddRange(FindObjectsByType<MeleeAttack>());
                list.AddRange(alsoDisable);
                foreach (Behaviour b in list)
                    if (b && b.enabled) { b.enabled = false; disabled.Add(b); }
            }
        }
        else
        {
            if (paused) { Time.timeScale = timeScaleBefore; paused = false; }
            foreach (Behaviour b in disabled) if (b) b.enabled = true;
            disabled.Clear();
        }
    }

    void MarkDirty() { dirty = true; }
    TMP_FontAsset Font => font ? font : (style ? style.font : null);

    // InventoryStyle rebuilt/resized its window: rebuild into it so sizes match
    void OnStyleApplied() { builtFor = null; }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        bool open = inventoryPanel && inventoryPanel.activeInHierarchy;
        if (open != wasOpen)
        {
            wasOpen = open;
            tab = Tab.Inventory;                  // always opens on the inventory
            if (builtFor) ApplyTab();
            SetGameplayPaused(open);
        }
        if (!open) return;

        if (style.Panel && builtFor != style.Panel) { Build(style.Panel); ApplyTab(); }
        if (!builtFor) return;

        if (Keyboard.current != null && Keyboard.current[switchTabKey].wasPressedThisFrame)
        {
            tab = tab == Tab.Inventory ? Tab.Quests : Tab.Inventory;
            ApplyTab();
        }
        if (dirty && tab == Tab.Quests) { Rebuild(); dirty = false; }
    }

    void LateUpdate()
    {
        if (!builtFor) return;
        // the tabs take the title's place, and the hint line explains both keys
        Transform title = builtFor.Find("Title");
        if (title && title.gameObject.activeSelf) title.gameObject.SetActive(false);
        Transform hint = builtFor.Find("Hint");
        if (hint && hint.TryGetComponent(out TextMeshProUGUI h))
        {
            string gold = "#" + ColorUtility.ToHtmlStringRGB(style.keyColor);
            string want = "<color=" + gold + ">" + KeyName(switchTabKey) + "</color> to switch    <color=" + gold + ">" + style.closeKey + "</color> to close";
            if (h.text != want) h.text = want;
        }
    }

    static string KeyName(Key k) => k == Key.Tab ? "TAB" : k.ToString().ToUpper();

    void ApplyTab()
    {
        bool quests = tab == Tab.Quests;
        // hide the slots (and anything else in the grid) while the quest log shows
        foreach (Slot s in inventoryPanel.GetComponentsInChildren<Slot>(true))
        {
            CanvasGroup g = s.GetComponent<CanvasGroup>();
            if (!g) g = s.gameObject.AddComponent<CanvasGroup>();
            g.alpha = quests ? 0f : 1f;
            g.blocksRaycasts = !quests;
        }
        if (logRoot) logRoot.gameObject.SetActive(quests);
        if (invTabImg)
        {
            StyleTab(invTabImg, invTabText, !quests);
            StyleTab(questTabImg, questTabText, quests);
        }
        if (quests) { Rebuild(); dirty = false; }
    }

    void StyleTab(Image img, TextMeshProUGUI text, bool selected)
    {
        img.sprite = selected ? style.HoverSprite : style.RimSprite;    // gold rim on the open tab
        text.color = selected ? style.textColor : style.hintColor * new Color(1f, 1f, 1f, 0.75f);
    }

    // ------------------------------------------------------------------ building (inside InventoryStyle's window)

    void Build(RectTransform panel)
    {
        if (tabsRoot) Destroy(tabsRoot.gameObject);
        if (logRoot) Destroy(logRoot.gameObject);
        builtFor = panel;

        // clicks need a raycaster on the inventory's canvas
        Canvas canvas = panel.GetComponentInParent<Canvas>();
        if (canvas && !canvas.rootCanvas.GetComponent<GraphicRaycaster>()) canvas.rootCanvas.gameObject.AddComponent<GraphicRaycaster>();

        float pad = style.panelPadding;
        float titleSpace = Mathf.Max(style.TitleSpace, style.titleFontSize * 1.6f);
        float hintSpace = style.HintSpace;

        // tabs across the top of the window, where the title was
        tabsRoot = NewRect("QuestTabs", panel);
        tabsRoot.anchorMin = new Vector2(0f, 1f); tabsRoot.anchorMax = Vector2.one; tabsRoot.pivot = new Vector2(0.5f, 1f);
        tabsRoot.offsetMin = new Vector2(pad, -titleSpace - pad * 0.4f);
        tabsRoot.offsetMax = new Vector2(-pad, -pad * 0.4f);
        float tabText = style.titleFontSize * 0.6f;
        MakeTab("InventoryTab", "INVENTORY", 0f, 0.5f, () => { tab = Tab.Inventory; ApplyTab(); }, tabText, out invTabImg, out invTabText);
        MakeTab("QuestsTab", "QUESTS", 0.5f, 1f, () => { tab = Tab.Quests; ApplyTab(); }, tabText, out questTabImg, out questTabText);

        // the quest list fills the space the slots use
        logRoot = NewRect("QuestLog", panel);
        logRoot.anchorMin = Vector2.zero; logRoot.anchorMax = Vector2.one;
        logRoot.offsetMin = new Vector2(pad, hintSpace + pad * 0.5f);
        logRoot.offsetMax = new Vector2(-pad, -(titleSpace + pad * 0.8f));
        logRoot.gameObject.AddComponent<RectMask2D>();
        var catcher = logRoot.gameObject.AddComponent<Image>();                 // lets the mouse wheel scroll
        catcher.color = new Color(0f, 0f, 0f, 0.001f);

        content = NewRect("Content", logRoot);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        logText = content.gameObject.AddComponent<TextMeshProUGUI>();
        if (Font) logText.font = Font;
        logText.fontSize = style.hintFontSize * stepTextSize;
        logText.color = style.textColor;
        logText.richText = true;
        logText.textWrappingMode = TextWrappingModes.Normal;
        logText.lineSpacing = 6f;
        logText.raycastTarget = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = logRoot.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = logRoot;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;
        content.offsetMax = new Vector2(-style.panelPadding * 0.6f, 0f);     // room for the scrollbar

        // thin scrollbar on the right (only shows when the list is longer than the window)
        RectTransform barRt = NewRect("Scrollbar", logRoot);
        barRt.anchorMin = new Vector2(1f, 0f); barRt.anchorMax = Vector2.one; barRt.pivot = new Vector2(1f, 0.5f);
        barRt.sizeDelta = new Vector2(style.panelPadding * 0.3f, 0f);
        var track = barRt.gameObject.AddComponent<Image>(); track.color = new Color(1f, 1f, 1f, 0.08f);
        RectTransform area = NewRect("Sliding Area", barRt);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = area.offsetMax = Vector2.zero;
        RectTransform handle = NewRect("Handle", area);
        handle.offsetMin = handle.offsetMax = Vector2.zero;
        var handleImg = handle.gameObject.AddComponent<Image>(); handleImg.color = style.rimColor;
        var bar = barRt.gameObject.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handle; bar.targetGraphic = handleImg;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        logRoot.gameObject.SetActive(false);
        dirty = true;
    }

    void MakeTab(string name, string label, float xMin, float xMax, UnityEngine.Events.UnityAction onClick, float textSizeUnits,
                 out Image img, out TextMeshProUGUI text)
    {
        RectTransform r = NewRect(name, tabsRoot);
        r.anchorMin = new Vector2(xMin, 0f); r.anchorMax = new Vector2(xMax, 1f);
        float gap = style.panelPadding * 0.2f;
        r.offsetMin = new Vector2(xMin > 0f ? gap : 0f, 0f);
        r.offsetMax = new Vector2(xMax < 1f ? -gap : 0f, 0f);
        img = r.gameObject.AddComponent<Image>();
        img.sprite = style.RimSprite; img.type = Image.Type.Sliced;
        var b = r.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(onClick);

        RectTransform tr = NewRect("Label", r);
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
        text = tr.gameObject.AddComponent<TextMeshProUGUI>();
        if (Font) text.font = Font;
        text.text = label;
        text.fontSize = textSizeUnits;
        text.characterSpacing = 4f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true; text.fontSizeMax = textSizeUnits; text.fontSizeMin = textSizeUnits * 0.5f;
        text.raycastTarget = false;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // ------------------------------------------------------------------ the log text

    void Rebuild()
    {
        if (!logText) return;
        var sb = new StringBuilder();
        string cream = Hex(style.textColor), gold = Hex(style.keyColor), hint = Hex(style.hintColor), dim = "#7A736B";
        int titleSize = Mathf.RoundToInt(style.hintFontSize * questTitleSize);
        int smallSize = Mathf.Max(1, Mathf.RoundToInt(style.hintFontSize * descriptionSize));

        bool any = false;
        var started = QuestManager.Started;
        for (int qi = started.Count - 1; qi >= 0; qi--)          // active quests, newest first
        {
            QuestData q = started[qi];
            if (QuestManager.GetState(q) != QuestManager.State.Active) continue;
            any = true;
            int current = QuestManager.CurrentStep(q);

            // quest name, its description, then what to do next (so nobody's lost), then finished steps crossed off
            sb.Append("<size=").Append(titleSize).Append("><color=").Append(cream).Append(">").Append(q.questName).Append("</color></size>\n");
            if (!string.IsNullOrEmpty(q.description))
                sb.Append("<size=").Append(smallSize).Append("><color=").Append(hint).Append(">").Append(q.description).Append("</color></size>\n");
            if (current < q.steps.Count)
                sb.Append("<color=").Append(gold).Append(">> ").Append(QuestManager.StepText(q, current)).Append(Count(q.steps[current])).Append("</color>\n");
            for (int i = 0; i < current && i < q.steps.Count; i++)
                sb.Append("<size=").Append(smallSize).Append("><color=").Append(dim).Append(">   <s>").Append(QuestManager.StepText(q, i)).Append("</s></color></size>\n");
            sb.Append("\n");
        }

        bool header = false;
        for (int qi = started.Count - 1; qi >= 0; qi--)          // finished quests, crossed off
        {
            QuestData q = started[qi];
            if (QuestManager.GetState(q) != QuestManager.State.Completed) continue;
            if (!header) { sb.Append("<size=").Append(smallSize).Append("><color=").Append(dim).Append(">- COMPLETED -</color></size>\n"); header = true; }
            any = true;
            sb.Append("<color=").Append(dim).Append("><s>").Append(q.questName).Append("</s></color>\n");
        }

        if (!any)
            sb.Append("<color=").Append(hint).Append(">No quests yet.\n\nTalk to folks with a <color=").Append(gold)
              .Append(">!</color> next to their name.</color>");

        logText.text = sb.ToString();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    static string Count(QuestStep s)
    {
        if ((s.type != QuestStepType.CollectItem && s.type != QuestStepType.BringItem) || s.item == null || s.amount <= 1) return "";
        return "  (" + Mathf.Min(QuestManager.CountItem(s.item), s.amount) + "/" + s.amount + ")";
    }

    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
}