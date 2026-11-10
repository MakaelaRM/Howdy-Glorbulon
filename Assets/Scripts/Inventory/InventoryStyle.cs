using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Restyles the inventory to match the 3D speech bubbles: dark rounded slots with a cream rim,
/// the same font, cream numbers, gold key hints, and a gold rim on the slot under the mouse.
/// Put it on the same object as your Inventory script. It styles the slots when the game starts,
/// so nothing in your scene or prefabs is changed (safe for teammates). Tweak values during Play to preview.
[DefaultExecutionOrder(100)]
public class InventoryStyle : MonoBehaviour
{
    [Header("Font")]
    [Tooltip("The font your speech bubbles use (Kleader). If empty, it's taken from the town HUD when there is one.")]
    public TMP_FontAsset font;

    [Header("Size")]
    [Tooltip("Overall size of the inventory. 1 = how it's laid out in your scene.")]
    [Range(0.3f, 1.5f)] public float uiScale = 0.65f;
    [Tooltip("Keep the whole panel on screen (moves it in if the title or bottom would be cut off).")]
    public bool keepOnScreen = true;
    [Tooltip("Minimum gap between the panel and the screen edge, in pixels.")]
    public float screenMargin = 24f;

    [Header("Colours (same as the speech bubbles)")]
    public Color faceColor = new Color(0.06f, 0.055f, 0.07f, 0.92f);
    public Color rimColor = new Color(0.92f, 0.84f, 0.68f, 1f);
    public Color hoverRimColor = new Color(0.96f, 0.78f, 0.42f, 1f);
    public Color textColor = new Color(0.98f, 0.95f, 0.88f, 1f);
    public Color hintColor = new Color(0.80f, 0.76f, 0.70f, 1f);
    public Color keyColor = new Color(0.96f, 0.78f, 0.42f, 1f);

    [Header("Slots")]
    [Tooltip("Rim thickness and corner size, like the bubble's bevel. Bigger = chunkier.")]
    [Range(0.3f, 3f)] public float rimScale = 1f;
    [Tooltip("Gap between the slot edge and the item icon, as a fraction of the slot size.")]
    [Range(0f, 0.4f)] public float iconPadding = 0.12f;
    [Tooltip("Item count text size, as a fraction of the slot size. Long numbers (like 1200) shrink to fit.")]
    [Range(0.05f, 0.5f)] public float amountFontSize = 0.24f;
    [Tooltip("Show the hotbar key (1-6) in the corner of the first slots.")]
    public bool showKeyNumbers = true;
    public int keyNumberCount = 6;
    [Tooltip("Key number size, as a fraction of the slot size.")]
    [Range(0.05f, 0.4f)] public float keyFontSize = 0.17f;
    [Tooltip("How much the slot under the mouse grows.")]
    public float hoverScale = 1.06f;

    [Header("Panel behind the slots")]
    public bool showPanel = true;
    public string title = "INVENTORY";
    [Tooltip("Small line under the panel. {key} is shown in gold.")]
    public string closeHint = "Press {key} to close";
    public string closeKey = "I";

    [Header("Money")]
    [Tooltip("Also show the player's money in the panel's top-left corner. Off by default because the Coins slot already shows it.")]
    public bool showMoney = false;
    [Tooltip("Optional: your coin icon sprite (the one from the town HUD).")]
    public Sprite coinIcon;
    public float moneyFontSize = 36f;
    public float titleFontSize = 40f;
    public float hintFontSize = 22f;
    [Tooltip("Space between the slots and the panel's edge, in pixels.")]
    public float panelPadding = 24f;

    readonly List<SlotLook> slots = new List<SlotLook>();
    Sprite rimSprite, hoverSprite;
    readonly List<Object> generated = new List<Object>();
    bool dirty;
    TextMeshProUGUI moneyText;
    Transform styledParent;
    bool styledWhileOpen;
    int shownMoney = int.MinValue;
    bool hasOriginal;
    Vector2 originalPos;
    Vector3 originalScale;

    class SlotLook
    {
        public Slot slot;
        public Image background;
        public bool hovered;
        public float scale = 1f;
    }

    void Start() { Apply(); }

    void OnValidate() { dirty = true; }

    void Update()
    {
        if (dirty && Application.isPlaying) { dirty = false; Apply(); }

        // Re-style the first time the inventory is opened: some text can only be finished while it's visible.
        bool open = styledParent && styledParent.gameObject.activeInHierarchy;
        if (open && !styledWhileOpen) { styledWhileOpen = true; Apply(); }

        if (moneyText && PlayerStats.Instance && shownMoney != PlayerStats.Instance.Money)
        {
            shownMoney = PlayerStats.Instance.Money;
            moneyText.text = (coinIcon ? "" : "$") + shownMoney;
        }

        float dt = Time.unscaledDeltaTime;
        foreach (SlotLook s in slots)
        {
            if (s.slot == null) continue;
            bool hover = s.slot.hovering && s.slot.gameObject.activeInHierarchy;
            if (hover != s.hovered)
            {
                s.hovered = hover;
                if (s.background) s.background.sprite = hover ? hoverSprite : rimSprite;
            }
            float target = hover ? hoverScale : 1f;
            s.scale = Mathf.MoveTowards(s.scale, target, dt * 1.5f);
            s.slot.transform.localScale = Vector3.one * s.scale;
        }
    }

    void OnDestroy()
    {
        foreach (Object o in generated) if (o) Destroy(o);
    }

    // ------------------------------------------------------------------ styling

    /// Re-applies the whole style (also runs when you change a value during Play).
    [ContextMenu("Apply Style")]
    public void Apply()
    {
        if (!Application.isPlaying) return;   // play mode only, so the scene file is never changed

        if (font == null)
        {
            TownHUD hud = FindAnyObjectByType<TownHUD>();
            TMP_Text t = hud ? hud.GetComponentInChildren<TMP_Text>(true) : null;
            if (t) font = t.font;
        }

        foreach (Object o in generated) if (o) Destroy(o);
        generated.Clear();
        int r = Mathf.Max(4, Mathf.RoundToInt(20 * rimScale)), rim = Mathf.Max(2, Mathf.RoundToInt(4 * rimScale));
        rimSprite = MakeRoundedSprite(r, rim, faceColor, rimColor);
        hoverSprite = MakeRoundedSprite(r, rim, faceColor, hoverRimColor);

        Inventory inventory = GetComponent<Inventory>();
        Transform slotParent = inventory && inventory.inventorySlotParent ? inventory.inventorySlotParent.transform : transform;

        styledParent = slotParent;

        // overall size: start from how it's laid out in the scene each time, so changes don't add up
        RectTransform sp = slotParent as RectTransform;
        if (sp)
        {
            if (!hasOriginal) { hasOriginal = true; originalPos = sp.anchoredPosition; originalScale = sp.localScale; }
            sp.anchoredPosition = originalPos;
            sp.localScale = originalScale * uiScale;
        }

        slots.Clear();
        Slot[] found = slotParent.GetComponentsInChildren<Slot>(true);
        int styled = 0;
        for (int i = 0; i < found.Length; i++)
        {
            // one bad slot shouldn't stop the rest from being styled
            try { StyleSlot(found[i], i); styled++; }
            catch (System.Exception e) { Debug.LogError($"[InventoryStyle] Couldn't style slot '{found[i].name}' (#{i + 1}): {e.Message}", found[i]); }
        }
        Debug.Log($"[InventoryStyle] Styled {styled} of {found.Length} slots under '{slotParent.name}'.", this);

        if (showPanel)
        {
            try { StylePanel(slotParent); }
            catch (System.Exception e) { Debug.LogError("[InventoryStyle] Couldn't build the panel: " + e.Message, this); }
        }
        else { Transform p = slotParent.Find("StylePanel"); if (p) p.gameObject.SetActive(false); }

        if (keepOnScreen && sp && sp.gameObject.activeInHierarchy) KeepOnScreen(sp);
    }

    // Moves the inventory so the whole panel (title included) is inside the screen.
    void KeepOnScreen(RectTransform sp)
    {
        Canvas canvas = sp.GetComponentInParent<Canvas>();
        if (!canvas) return;
        RectTransform root = canvas.rootCanvas.transform as RectTransform;
        Canvas.ForceUpdateCanvases();

        Transform panelT = sp.Find("StylePanel");
        RectTransform box = panelT && panelT.gameObject.activeSelf ? (RectTransform)panelT : sp;
        var corners = new Vector3[4];
        box.GetWorldCorners(corners);
        Vector2 min = root.InverseTransformPoint(corners[0]);
        Vector2 max = root.InverseTransformPoint(corners[2]);
        Rect area = root.rect;
        float scale = root.localScale.x > 0f ? 1f : 1f;
        float m = screenMargin * scale;

        float dx = 0f, dy = 0f;
        if (max.x - min.x <= area.width - 2 * m)
        {
            if (min.x < area.xMin + m) dx = area.xMin + m - min.x;
            else if (max.x > area.xMax - m) dx = area.xMax - m - max.x;
        }
        if (max.y - min.y <= area.height - 2 * m)
        {
            if (max.y > area.yMax - m) dy = area.yMax - m - max.y;
            else if (min.y < area.yMin + m) dy = area.yMin + m - min.y;
        }
        if (dx != 0f || dy != 0f) sp.position += root.TransformVector(new Vector3(dx, dy, 0f));
    }

    void StyleSlot(Slot slot, int index)
    {
        var look = new SlotLook { slot = slot };

        // sizes are relative to the slot, so they look right whatever size your slots are
        float size = SlotSize(slot);

        // background
        Image bg = slot.GetComponent<Image>();
        if (bg)
        {
            bg.sprite = rimSprite;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f;
            bg.color = Color.white;
            look.background = bg;
        }

        // icon (child 0): inset so it doesn't touch the rim
        if (slot.transform.childCount > 0)
        {
            RectTransform icon = slot.transform.GetChild(0) as RectTransform;
            if (icon)
            {
                icon.anchorMin = Vector2.zero; icon.anchorMax = Vector2.one;
                float pad = iconPadding * size;
                icon.offsetMin = Vector2.one * pad; icon.offsetMax = -Vector2.one * pad;
                Image img = icon.GetComponent<Image>();
                if (img) img.preserveAspect = true;
            }
        }

        // amount (child 1): cream number in the bottom-right corner
        if (slot.transform.childCount > 1)
        {
            TextMeshProUGUI amount = slot.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
            if (amount)
            {
                float fs = amountFontSize * size;
                StyleText(amount, fs, textColor, TextAlignmentOptions.BottomRight);
                // shrink long numbers so they stay inside the bottom-right corner
                amount.enableAutoSizing = true;
                amount.fontSizeMax = fs;
                amount.fontSizeMin = fs * 0.4f;
                RectTransform rt = amount.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(1f, 0f);
                rt.offsetMin = new Vector2(size * 0.1f, size * 0.06f);
                rt.offsetMax = new Vector2(-size * 0.09f, size * 0.06f + fs * 1.1f);
            }
        }

        // hotbar key number in the top-left corner (added at the end so Slot.cs's child 0/1 stay the same)
        Transform keyLabel = slot.transform.Find("KeyHint");
        bool wantKey = showKeyNumbers && index < keyNumberCount;
        if (wantKey)
        {
            TextMeshProUGUI k;
            if (keyLabel == null)
            {
                var go = new GameObject("KeyHint", typeof(RectTransform));
                go.transform.SetParent(slot.transform, false);
                k = go.AddComponent<TextMeshProUGUI>();
                keyLabel = go.transform;
            }
            else k = keyLabel.GetComponent<TextMeshProUGUI>();
            keyLabel.gameObject.SetActive(true);
            keyLabel.SetAsLastSibling();
            StyleText(k, keyFontSize * size, keyColor, TextAlignmentOptions.TopLeft);
            k.text = (index + 1).ToString();
            RectTransform rt = k.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(size * 0.1f, size * 0.06f); rt.offsetMax = new Vector2(-size * 0.06f, -size * 0.07f);
        }
        else if (keyLabel) keyLabel.gameObject.SetActive(false);

        slots.Add(look);
    }

    void StylePanel(Transform slotParent)
    {
        Transform existing = slotParent.Find("StylePanel");
        GameObject panel = existing ? existing.gameObject : new GameObject("StylePanel", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        panel.SetActive(true);
        panel.transform.SetParent(slotParent, false);
        panel.transform.SetAsFirstSibling();                       // drawn behind the slots
        panel.GetComponent<LayoutElement>().ignoreLayout = true;   // doesn't take a spot in the grid

        float titleSpace = string.IsNullOrEmpty(title) ? 0f : titleFontSize * 1.6f;
        float hintSpace = string.IsNullOrEmpty(closeHint) ? 0f : hintFontSize * 1.8f;
        RectTransform rt = (RectTransform)panel.transform;
        Rect slotsBox = SlotBounds(slotParent);
        if (slotsBox.width > 1f)
        {
            // hug the slots themselves (not the whole slot area), so there's no empty space
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(slotsBox.width + panelPadding * 2f, slotsBox.height + panelPadding * 2f + titleSpace + hintSpace);
            rt.localPosition = new Vector3(slotsBox.center.x, slotsBox.center.y + (titleSpace - hintSpace) * 0.5f, 0f);
        }
        else
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-panelPadding, -panelPadding - hintSpace);
            rt.offsetMax = new Vector2(panelPadding, panelPadding + titleSpace);
        }

        Image img = panel.GetComponent<Image>();
        img.sprite = rimSprite; img.type = Image.Type.Sliced; img.color = Color.white; img.raycastTarget = false;

        TextMeshProUGUI titleText = Label(panel.transform, "Title");
        titleText.gameObject.SetActive(titleSpace > 0f);
        if (titleSpace > 0f)
        {
            StyleText(titleText, titleFontSize, textColor, TextAlignmentOptions.Center);
            titleText.characterSpacing = 6f;
            titleText.text = title;
            RectTransform t = titleText.rectTransform;
            t.anchorMin = new Vector2(0f, 1f); t.anchorMax = Vector2.one; t.pivot = new Vector2(0.5f, 1f);
            t.offsetMin = new Vector2(0f, -titleSpace - panelPadding * 0.5f); t.offsetMax = new Vector2(0f, -panelPadding * 0.5f);
        }

        // money counter, top-left of the panel (same row as the title)
        Transform money = panel.transform.Find("Money");
        if (showMoney)
        {
            if (money == null)
            {
                money = new GameObject("Money", typeof(RectTransform)).transform;
                money.SetParent(panel.transform, false);
            }
            money.gameObject.SetActive(true);
            RectTransform m = (RectTransform)money;
            m.anchorMin = m.anchorMax = new Vector2(0f, 1f); m.pivot = new Vector2(0f, 1f);
            float rowH = Mathf.Max(moneyFontSize * 1.3f, 10f);
            m.anchoredPosition = new Vector2(panelPadding, -panelPadding * 0.5f - (titleSpace - rowH) * 0.5f);
            m.sizeDelta = new Vector2(300f, rowH);

            Transform iconT = money.Find("Icon");
            Image icon = iconT ? iconT.GetComponent<Image>() : new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(money, false);
            icon.gameObject.SetActive(coinIcon != null);
            icon.sprite = coinIcon; icon.preserveAspect = true; icon.raycastTarget = false;
            RectTransform ir = icon.rectTransform;
            ir.anchorMin = new Vector2(0f, 0f); ir.anchorMax = new Vector2(0f, 1f); ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = Vector2.zero; ir.sizeDelta = new Vector2(rowH, 0f);

            moneyText = Label(money, "Amount");
            StyleText(moneyText, moneyFontSize, keyColor, TextAlignmentOptions.Left);
            RectTransform tr = moneyText.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(coinIcon ? rowH + 10f : 0f, 0f); tr.offsetMax = Vector2.zero;
            shownMoney = int.MinValue;   // refresh next frame
            if (!coinIcon) moneyText.text = "$0";
        }
        else if (money) money.gameObject.SetActive(false);

        TextMeshProUGUI hint = Label(panel.transform, "Hint");
        hint.gameObject.SetActive(hintSpace > 0f);
        if (hintSpace > 0f)
        {
            StyleText(hint, hintFontSize, hintColor, TextAlignmentOptions.Center);
            hint.characterSpacing = 4f;
            hint.text = closeHint.Replace("{key}", "<color=#" + ColorUtility.ToHtmlStringRGB(keyColor) + ">" + closeKey + "</color>");
            RectTransform h = hint.rectTransform;
            h.anchorMin = Vector2.zero; h.anchorMax = new Vector2(1f, 0f); h.pivot = new Vector2(0.5f, 0f);
            h.offsetMin = new Vector2(0f, panelPadding * 0.4f); h.offsetMax = new Vector2(0f, panelPadding * 0.4f + hintSpace);
        }
    }

    // Height of a slot in its own units (from the grid's cell size if the layout hasn't run yet).
    static float SlotSize(Slot slot)
    {
        var rt = slot.transform as RectTransform;
        float h = rt ? Mathf.Min(rt.rect.width, rt.rect.height) : 0f;
        if (h < 1f && slot.transform.parent)
        {
            GridLayoutGroup grid = slot.transform.parent.GetComponent<GridLayoutGroup>();
            if (grid) h = Mathf.Min(grid.cellSize.x, grid.cellSize.y);
        }
        return h > 1f ? h : 100f;
    }

    // Box around all the slots, in the slot parent's own space.
    Rect SlotBounds(Transform slotParent)
    {
        if (slotParent is RectTransform prt && prt.gameObject.activeInHierarchy)
            LayoutRebuilder.ForceRebuildLayoutImmediate(prt);

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        var corners = new Vector3[4];
        bool any = false;
        foreach (SlotLook s in slots)
        {
            if (!s.slot) continue;
            var rt = s.slot.transform as RectTransform;
            if (!rt) continue;
            Vector3 keep = rt.localScale; rt.localScale = Vector3.one;   // ignore the hover grow
            rt.GetWorldCorners(corners);
            rt.localScale = keep;
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = slotParent.InverseTransformPoint(corners[i]);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            any = true;
        }
        return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : default;
    }

    static TextMeshProUGUI Label(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t) return t.GetComponent<TextMeshProUGUI>();
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.AddComponent<TextMeshProUGUI>();
    }

    void StyleText(TextMeshProUGUI t, float size, Color color, TextAlignmentOptions align)
    {
        if (font) t.font = font;
        t.fontSize = size;
        t.enableAutoSizing = false;
        t.color = color;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.characterSpacing = 2f;
        // dark outline so numbers read on top of bright icons
        // Text on a switched-off object isn't set up yet, so the outline is added once the inventory is open.
        if (t.isActiveAndEnabled && t.fontSharedMaterial != null)
        {
            t.outlineWidth = 0.18f;
            t.outlineColor = new Color32(12, 10, 16, 255);
        }
    }

    // Rounded rectangle with a rim, 9-sliced so it stretches to any slot size.
    Sprite MakeRoundedSprite(int radius, int rim, Color face, Color rimCol)
    {
        int border = radius + 4;
        int size = border * 2 + 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "InventorySlot (generated)"
        };
        var px = new Color[size * size];
        float half = size * 0.5f;
        float inner = half - 1f;                                   // 1px margin for anti-aliasing
        Color faceTop = Color.Lerp(face, Color.white, 0.06f);       // faint top light, like the bubble's bevel
        faceTop.a = face.a;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // signed distance to a rounded box
            float qx = Mathf.Abs(x + 0.5f - half) - (inner - radius);
            float qy = Mathf.Abs(y + 0.5f - half) - (inner - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;

            float alpha = Mathf.Clamp01(0.5f - outside);                    // shape edge
            float rimMix = Mathf.Clamp01(outside + rim + 0.5f);             // 1 near the edge, 0 inside
            float up = y / (float)size;                                     // 0 bottom .. 1 top

            // rounded bevel like the 3D bubble: bright in the middle of the rim, darker at its edges,
            // lit from above (lighter top edge, darker bottom edge)
            float across = Mathf.Clamp01(-outside / Mathf.Max(1f, rim));    // 0 outer edge .. 1 inner edge
            float bevel = Mathf.Lerp(0.72f, 1.06f, Mathf.Sin(across * Mathf.PI));
            float light = Mathf.Lerp(0.82f, 1.08f, up);
            Color rc = rimCol * (bevel * light); rc.a = rimCol.a;

            // thin shadow where the rim meets the face
            float shadow = Mathf.Clamp01(1f - Mathf.Abs(-outside - rim - 1f) / 1.5f) * 0.6f;
            Color f = Color.Lerp(face, faceTop, up);
            f = Color.Lerp(f, new Color(0f, 0f, 0f, f.a), shadow);

            Color c = Color.Lerp(f, rc, rimMix);
            c.a *= alpha;
            px[y * size + x] = c;
        }
        tex.SetPixels(px);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        sprite.name = tex.name;
        generated.Add(tex);
        generated.Add(sprite);
        return sprite;
    }
}