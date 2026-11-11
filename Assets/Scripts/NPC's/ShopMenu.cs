using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// Screen-space shop menu that takes over the left half of the screen while the player shops with an NPC.
/// Add it to an empty GameObject in the scene; it builds its own canvas and layout at runtime.
public class ShopMenu : MonoBehaviour
{
    public const int ItemsPerPage = 4;

    [Header("Assets")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite coinIcon;
    [SerializeField] private Sprite panelSprite;

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.92f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color statsColor = new Color(0.82f, 0.8f, 0.78f);
    [Tooltip("Color for key names like \"1\" or \"Esc\". Matches the speech bubbles' key color by default.")]
    [SerializeField] private Color keyColor = new Color(0.96f, 0.78f, 0.42f);
    [SerializeField] private Color costColor = new Color(1f, 0.82f, 0.2f);
    [SerializeField] private Color canBuyColor = new Color(0.4f, 1f, 0.45f);
    [SerializeField] private Color cannotBuyColor = new Color(1f, 0.35f, 0.3f);

    [Header("Controls")]
    [Tooltip("Shows the next page when an NPC sells more than 4 items.")]
    [SerializeField] private InputAction nextPageAction = new InputAction("ShopNextPage", InputActionType.Button, "<Keyboard>/tab");

    [Header("Animation")]
    [SerializeField] private float openDuration = 0.25f;
    [SerializeField] private float slideDistance = 80f;
    [SerializeField] private float purchasedFlashTime = 0.8f;

    private const float Padding = 48f;
    private const float SlotTop = 150f;
    private const float SlotHeight = 175f;

    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform panel;
    private Vector2 panelHome;
    private TextMeshProUGUI titleText, walletText, pageText, footerText;
    private Slot[] slots;

    private IReadOnlyList<ShopItem> items;
    private int page;
    private bool isOpen;
    private float visibility;
    private int flashSlot = -1;
    private float flashTimer;
    private int shownMoney = int.MinValue;

    public bool IsOpen => isOpen;

    private int PageCount => items == null || items.Count == 0 ? 1 : (items.Count + ItemsPerPage - 1) / ItemsPerPage;

    void Reset()
    {
        #if UNITY_EDITOR
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("kleader t:TMP_FontAsset"))
            {
                font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (font != null) break;
            }
            coinIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Icons/Icon_Money.png");
            panelSprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        #endif
    }

    void Awake()
    {
        BuildMenu();
        group.alpha = 0f;
        canvas.enabled = false;
    }

    void OnEnable() => nextPageAction.Enable();
    void OnDisable() => nextPageAction.Disable();

    public void Open(NPCInteractable npc, string exitKeyName)
    {
        items = npc.ShopItems;
        page = 0;
        flashTimer = 0f;
        shownMoney = int.MinValue;
        isOpen = true;

        titleText.text = npc.ShopTitle;
        footerText.text = "Press " + KeyHint(exitKeyName) + " to escape";
        ShowPage();
        Refresh();
    }

    public void Close()
    {
        isOpen = false;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float target = isOpen ? 1f : 0f;
        visibility = openDuration > 0f ? Mathf.MoveTowards(visibility, target, dt / openDuration) : target;

        float eased = 1f - (1f - visibility) * (1f - visibility);
        group.alpha = visibility;
        panel.anchoredPosition = panelHome + Vector2.left * (slideDistance * (1f - eased));
        canvas.enabled = visibility > 0f;

        if (!isOpen) return;

        if (flashTimer > 0f) flashTimer -= dt;
        HandleInput();
        Refresh();
    }

    private void HandleInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            for (int i = 0; i < ItemsPerPage; i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
                    TryPurchase(i);
            }
        }

        if (PageCount > 1 && nextPageAction.WasPressedThisFrame())
        {
            page = (page + 1) % PageCount;
            flashTimer = 0f;
            ShowPage();
        }
    }

    private void TryPurchase(int slot)
    {
        int index = page * ItemsPerPage + slot;
        if (items == null || index >= items.Count || items[index] == null) return;

        PlayerStats stats = PlayerStats.Instance;
        if (stats == null)
        {
            Debug.LogWarning("The shop can't find a PlayerStats in the scene.", this);
            return;
        }

        Inventory inventory = Inventory.Find();
        if (inventory == null)
        {
            Debug.LogWarning("The shop can't find an Inventory in the scene.", this);
            return;
        }

        ShopItem item = items[index];
        if (stats.Money < item.cost) return;
        if (!item.FitsIn(inventory))
        {
            Debug.Log("Not enough inventory space to buy " + item.DisplayName + ".", this);
            return;
        }

        stats.AddMoney(-item.cost);
        item.ApplyTo(inventory);

        flashSlot = slot;
        flashTimer = purchasedFlashTime;
    }

    private void ShowPage()
    {
        for (int i = 0; i < ItemsPerPage; i++)
        {
            Slot slot = slots[i];
            int index = page * ItemsPerPage + i;
            ShopItem item = items != null && index < items.Count ? items[index] : null;

            slot.root.SetActive(item != null);
            if (item == null) continue;

            string keyName = (i + 1).ToString();
            slot.key.text = keyName;
            slot.label.text = item.DisplayName + ":";
            slot.cost.text = item.cost.ToString();
            slot.stats.text = item.DescribeEffects();
            slot.buyText = "Press " + KeyHint(keyName) + " to purchase";
            slot.shownState = -1;
        }

        pageText.text = PageCount > 1
            ? "Page " + (page + 1) + "/" + PageCount + "   Press " + KeyHint(nextPageAction.GetBindingDisplayString()) + " for more"
            : string.Empty;
    }

    private void Refresh()
    {
        PlayerStats stats = PlayerStats.Instance;
        int money = stats != null ? stats.Money : 0;

        if (money != shownMoney)
        {
            shownMoney = money;
            walletText.text = money.ToString();
        }

        for (int i = 0; i < ItemsPerPage; i++)
        {
            Slot slot = slots[i];
            if (!slot.root.activeSelf) continue;

            ShopItem item = items[page * ItemsPerPage + i];
            bool affordable = money >= item.cost;
            int state = flashSlot == i && flashTimer > 0f ? 2 : affordable ? 1 : 0;
            if (state == slot.shownState) continue;
            slot.shownState = state;

            slot.cost.color = affordable ? costColor : cannotBuyColor;
            if (state == 2)
            {
                slot.notice.text = "Purchased!";
                slot.notice.color = canBuyColor;
            }
            else if (state == 1)
            {
                slot.notice.text = slot.buyText;
                slot.notice.color = canBuyColor;
            }
            else
            {
                slot.notice.text = "Insufficient Funds";
                slot.notice.color = cannotBuyColor;
            }
        }
    }

    private string KeyHint(string keyName)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGB(keyColor) + ">" + keyName + "</color>";
    }

    private void BuildMenu()
    {
        var canvasObject = new GameObject("ShopCanvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 20;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        group = canvasObject.AddComponent<CanvasGroup>();
        group.ignoreParentGroups = true;
        group.interactable = false;
        group.blocksRaycasts = false;

        panel = CreateRect("Panel", canvasObject.transform);
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = new Vector2(0.5f, 1f);
        panel.offsetMin = new Vector2(40f, 40f);
        panel.offsetMax = new Vector2(-20f, -40f);
        panelHome = panel.anchoredPosition;
        Style(panel.gameObject.AddComponent<Image>(), panelSprite, panelColor);

        titleText = CreateText("Title", panel, 56f, TextAlignmentOptions.MidlineLeft, textColor);
        PlaceTop(titleText.rectTransform, Padding, 260f, 36f, 72f);
        titleText.fontStyle = FontStyles.UpperCase;
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 30f;
        titleText.fontSizeMax = 56f;
        titleText.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform wallet = CreateRect("Wallet", panel);
        wallet.anchorMin = wallet.anchorMax = new Vector2(1f, 1f);
        wallet.pivot = new Vector2(1f, 0.5f);
        wallet.anchoredPosition = new Vector2(-Padding, -72f);
        ConfigureRow(wallet.gameObject.AddComponent<HorizontalLayoutGroup>(), 10f);
        var walletFit = wallet.gameObject.AddComponent<ContentSizeFitter>();
        walletFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        walletFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        CreateCoin(wallet, 40f);
        walletText = CreateText("WalletText", wallet, 40f, TextAlignmentOptions.MidlineLeft, costColor);

        Image underline = CreateImage("Underline", panel, null, textColor);
        PlaceTop(underline.rectTransform, Padding, Padding, 122f, 4f);

        slots = new Slot[ItemsPerPage];
        for (int i = 0; i < ItemsPerPage; i++)
            slots[i] = CreateSlot(i);

        pageText = CreateText("Page", panel, 28f, TextAlignmentOptions.Center, statsColor);
        PlaceBottom(pageText.rectTransform, Padding, Padding, 104f, 36f);

        footerText = CreateText("Footer", panel, 40f, TextAlignmentOptions.Center, textColor);
        PlaceBottom(footerText.rectTransform, Padding, Padding, 36f, 56f);
    }

    private Slot CreateSlot(int index)
    {
        var slot = new Slot();

        RectTransform root = CreateRect("Item" + (index + 1), panel);
        PlaceTop(root, Padding, Padding, SlotTop + index * SlotHeight, SlotHeight);
        slot.root = root.gameObject;

        RectTransform header = CreateRect("Header", root);
        PlaceTop(header, 0f, 0f, 0f, 60f);
        ConfigureRow(header.gameObject.AddComponent<HorizontalLayoutGroup>(), 16f);

        RectTransform keyBox = CreateRect("KeyBox", header);
        SetLayoutSize(keyBox, 56f, 56f);
        Style(keyBox.gameObject.AddComponent<Image>(), null, textColor);
        Stretch(CreateImage("Inner", keyBox, null, Color.black).rectTransform, 4f);
        slot.key = CreateText("Key", keyBox, 36f, TextAlignmentOptions.Center, keyColor);
        Stretch(slot.key.rectTransform, 0f);

        slot.label = CreateText("Name", header, 44f, TextAlignmentOptions.MidlineLeft, textColor);
        CreateCoin(header, 40f);
        slot.cost = CreateText("Cost", header, 44f, TextAlignmentOptions.MidlineLeft, costColor);

        slot.stats = CreateText("Stats", root, 30f, TextAlignmentOptions.MidlineLeft, statsColor);
        PlaceTop(slot.stats.rectTransform, 72f, 0f, 68f, 40f);
        slot.stats.overflowMode = TextOverflowModes.Ellipsis;

        slot.notice = CreateText("Notice", root, 32f, TextAlignmentOptions.MidlineLeft, canBuyColor);
        PlaceTop(slot.notice.rectTransform, 72f, 0f, 112f, 44f);

        root.gameObject.SetActive(false);
        return slot;
    }

    private void CreateCoin(Transform parent, float size)
    {
        Image coin = CreateImage("Coin", parent, coinIcon, coinIcon != null ? Color.white : costColor);
        coin.preserveAspect = true;
        SetLayoutSize(coin.rectTransform, size, size);
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, float size, TextAlignmentOptions alignment, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.richText = true;
        text.raycastTarget = false;
        text.text = string.Empty;
        return text;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        Style(image, sprite, color);
        return image;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Style(Image image, Sprite sprite, Color color)
    {
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
    }

    private static void ConfigureRow(HorizontalLayoutGroup row, float spacing)
    {
        row.spacing = spacing;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
    }

    private static void SetLayoutSize(RectTransform rect, float width, float height)
    {
        var element = rect.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = width;
        element.minHeight = element.preferredHeight = height;
    }

    private static void PlaceTop(RectTransform rect, float left, float right, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(-(left + right), height);
        rect.anchoredPosition = new Vector2((left - right) * 0.5f, -top);
    }

    private static void PlaceBottom(RectTransform rect, float left, float right, float bottom, float height)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(-(left + right), height);
        rect.anchoredPosition = new Vector2((left - right) * 0.5f, bottom);
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private class Slot
    {
        public GameObject root;
        public TextMeshProUGUI key, label, cost, stats, notice;
        public string buyText;
        public int shownState = -1;
    }
}

/// Something an NPC sells: a name, a price, and what it does to the player when bought.
[Serializable]
public class ShopItem
{
    public string itemName = "New Item";
    [Min(0)] public int cost = 10;
    public List<ShopItemEffect> effects = new List<ShopItemEffect>();

    public string DisplayName => string.IsNullOrEmpty(itemName) ? "Item" : itemName;

    public string DescribeEffects()
    {
        if (effects == null || effects.Count == 0) return string.Empty;

        var builder = new StringBuilder();
        foreach (ShopItemEffect effect in effects)
        {
            if (effect == null) continue;
            if (builder.Length > 0) builder.Append(", ");
            builder.Append(effect.stat).Append(": ").Append(effect.amount);
        }
        return builder.ToString();
    }

    public bool FitsIn(Inventory inventory)
    {
        if (effects == null) return true;

        int food = 0, drinks = 0;
        foreach (ShopItemEffect effect in effects)
        {
            if (effect == null || effect.amount <= 0) continue;
            if (effect.stat == ShopStat.Food) food += effect.amount;
            else if (effect.stat == ShopStat.Drinks) drinks += effect.amount;
        }
        return inventory.HasRoomForFoodAndDrinks(food, drinks);
    }

    public void ApplyTo(Inventory inventory)
    {
        if (effects == null) return;

        foreach (ShopItemEffect effect in effects)
        {
            if (effect == null) continue;
            switch (effect.stat)
            {
                case ShopStat.Food:
                    inventory.AddFood(effect.amount);
                    break;
                case ShopStat.Drinks:
                    inventory.AddDrinks(effect.amount);
                    break;
            }
        }
    }
}

/// One stat a shop item changes, shown in the menu as "Stat: amount".
[Serializable]
public class ShopItemEffect
{
    public ShopStat stat = ShopStat.Food;
    public int amount = 10;
}

/// The stats a shop item can affect. Add new entries here (and a case in ShopItem.ApplyTo) for blasters, suits, etc.
public enum ShopStat
{
    Food,
    Drinks
}
