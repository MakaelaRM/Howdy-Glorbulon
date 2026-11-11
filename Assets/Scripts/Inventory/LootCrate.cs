using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// Loot crate the player opens with E.
/// Put it on the root of a placed Prop_LootCrate_Common / _Rare / _Legendary model.
/// It finds the "_Lid" and "_Sparkles" children by itself.
/// Opening: a little shake, the lid pops open with a bounce, the crate squashes, the glow dims, and coins
/// burst out and fly into the player. Then the rewards are given (see the Rewards section).
public class LootCrate : MonoBehaviour
{
    [Header("Interact")]
    public Key interactKey = Key.E;
    [Tooltip("How close the player must be (metres from the crate's centre).")]
    public float interactRange = 2f;
    public string playerTag = "Player";
    [Tooltip("Show a prompt bubble above the crate when the player is close.")]
    public bool showPrompt = true;
    [Tooltip("Big top line of the bubble. Leave empty to show only the action line.")]
    public string promptTitle = "Loot Crate";
    [Tooltip("The prompt text. {key} is replaced with the interact key, shown in yellow.")]
    public string promptAction = "Press {key} to open";
    [Tooltip("Optional: the font your NPC bubbles use, so the crate prompt matches.")]
    public TMP_FontAsset promptFont;
    [Tooltip("Height of the prompt above the crate's base, in metres.")]
    public float promptHeight = 1.1f;
    [Tooltip("Draw the dark speech-bubble panel (with a little tail) behind the text, like the NPC prompts.")]
    public bool promptBackground = true;
    [Tooltip("Text size on screen (at 1080p). The prompt is drawn on the screen like the HUD, so it's always readable.")]
    public float promptFontSize = 34f;
    [Tooltip("Your game's bubble: drag the ChatBubble prefab here (the same one the NPCs use). " +
             "Title goes on the big line, the action on the prompt line. Leave empty for the built-in prompt.")]
    public GameObject promptPrefab;
    [Tooltip("Size of the bubble prefab. 1 = same as the prefab. Make it smaller if it looks too big over a crate.")]
    public float promptScale = 1f;

    [Header("Rewards")]
    [Tooltip("Optional: your Inventory. Leave empty to find it automatically.")]
    public Inventory inventoryOverride;
    [Tooltip("The healing item to give (drag your Health Item SO here).")]
    public ItemSO healingItem;
    [Tooltip("How many healing items this crate gives.")]
    public int healingItems = 1;
    [Tooltip("Any other items this crate gives.")]
    public List<ItemReward> extraItems = new List<ItemReward>();
    [Tooltip("Money given when opened. Added to PlayerStats, so the Coins slot in the inventory shows it.")]
    public int currency = 25;
    [Tooltip("Fires with the money amount (extra hook; PlayerStats is already updated for you).")]
    public UnityEvent<int> onGiveCurrency;
    [Tooltip("Fires once when the crate opens (sounds, particles, quest progress...).")]
    public UnityEvent onOpened;

    [System.Serializable]
    public struct ItemReward { public ItemSO item; public int amount; }

    [Header("Animation")]
    public float shakeTime = 0.18f;
    public float openAngle = 112f;
    public float openTime = 0.45f;
    [Tooltip("Tick this if the lid swings the wrong way.")]
    public bool invertLid = false;
    [Tooltip("Number of coins that burst out (just for show).")]
    [Range(0, 30)] public int coinBurst = 10;
    public Color coinColor = new Color(1f, 0.78f, 0.24f);

    [Header("Coin Amount Popup")]
    [Tooltip("Show the number of coins (e.g. +25) floating up from the crate as it opens.")]
    public bool showCoinAmount = true;
    [Tooltip("{0} is replaced by the amount.")]
    public string coinPopupFormat = "+{0} coins";
    public Color coinAmountColor = new Color(0.96f, 0.78f, 0.42f);
    [Tooltip("Text size in the world (about 0.1 m per point).")]
    public float coinAmountSize = 4f;
    [Tooltip("How long it stays up, in seconds.")]
    public float coinAmountTime = 1.4f;
    [Tooltip("Also show the items you got (e.g. +1 Health Potion) under the coins.")]
    public bool showItemAmounts = true;
    [Tooltip("{0} = amount, {1} = item name.")]
    public string itemAmountFormat = "+{0} {1}";
    [Tooltip("Text for healing items. {0} = amount.")]
    public string medsPopupFormat = "+{0} meds";
    [Tooltip("Colour for healing items (the potion's blue).")]
    public Color medsColor = new Color(0.36f, 0.86f, 0.95f);
    public Color otherItemColor = new Color(0.98f, 0.95f, 0.88f);
    [Tooltip("Delay between each line popping up, in seconds.")]
    public float popupLineDelay = 0.2f;

    [Header("Looted look")]
    [Tooltip("Glowing materials on this crate and the dim material to swap each one to once it is looted (e.g. Mat_LootGlow -> Mat_BadgeLegendDim).")]
    public MaterialSwap[] dimWhenLooted;
    [System.Serializable] public struct MaterialSwap { public Material glowing; public Material dim; }

    [Header("Sound (optional)")]
    public AudioClip openSound;
    public AudioClip coinSound;

    public bool IsLooted { get; private set; }

    Transform lid, sparkles, player;
    Quaternion lidClosed;
    Vector3 baseScale;
    GameObject prompt;
    AudioSource audioSource;
    Material coinMat;

    void Awake()
    {
        foreach (Transform c in GetComponentsInChildren<Transform>(true))
        {
            if (c.name.EndsWith("_Lid")) lid = c;
            else if (c.name.EndsWith("_Sparkles")) sparkles = c;
        }
        if (lid) lidClosed = lid.localRotation;
        baseScale = transform.localScale;
        audioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        GameObject p = null;
        try { p = GameObject.FindWithTag(playerTag); } catch (UnityException) { }
        if (p) player = p.transform;
        if (showPrompt) prompt = promptPrefab ? UsePromptPrefab() : MakePrompt();
        if (bubble) bubble.HideImmediate();
        else if (prompt) prompt.SetActive(false);
    }

    /// Shows or hides the prompt. Your SpeechBubble3D gets Show()/Hide() so it pops in and out like the NPC bubbles.
    void SetPromptVisible(bool visible)
    {
        if (!prompt) return;
        if (bubble)
        {
            if (visible && !bubble.IsShown) bubble.Show();
            else if (!visible && bubble.IsShown) bubble.Hide();
        }
        else if (prompt.activeSelf != visible) prompt.SetActive(visible);
    }

    void Update()
    {
        if (IsLooted || player == null) { SetPromptVisible(false); return; }

        Vector3 to = player.position - transform.position; to.y = 0f;
        bool inRange = to.magnitude <= interactRange;
        if (prompt)
        {
            if (screenPrompt)
            {
                // keep the on-screen prompt above the crate
                Vector3 sp = Camera.main ? Camera.main.WorldToScreenPoint(transform.position + Vector3.up * promptHeight) : Vector3.zero;
                SetPromptVisible(inRange && sp.z > 0f);
                if (inRange && sp.z > 0f) prompt.transform.position = new Vector3(sp.x, sp.y, 0f);
            }
            else
            {
                SetPromptVisible(inRange);
                // 3D bubble: sit above the crate and face the camera (its front is -Z, so it matches the camera's rotation)
                prompt.transform.position = transform.position + Vector3.up * promptHeight;
                if (Camera.main) prompt.transform.rotation = Camera.main.transform.rotation;
            }
        }

        if (inRange && Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame) Open();
    }

    /// Opens the crate (also callable from other scripts).
    public void Open()
    {
        if (IsLooted) return;
        IsLooted = true;
        SetPromptVisible(false);
        StartCoroutine(OpenRoutine());
    }

    /// For crates that were already looted when a save loads: lid open and dim, no rewards.
    public void SetLootedInstantly()
    {
        IsLooted = true;
        ApplyLootedLook();
        if (lid) lid.localRotation = lidClosed * Quaternion.Euler(LidAngle(), 0f, 0f);
        SetPromptVisible(false);
    }

    float LidAngle() => invertLid ? openAngle : -openAngle;

    IEnumerator OpenRoutine()
    {
        // 1. anticipation: quick rattle
        Vector3 basePos = transform.position;
        for (float t = 0f; t < shakeTime; t += Time.deltaTime)
        {
            float a = Mathf.Sin(t * 70f) * 0.025f * (1f - t / shakeTime);
            transform.position = basePos + transform.right * a;
            yield return null;
        }
        transform.position = basePos;

        if (openSound && audioSource) audioSource.PlayOneShot(openSound);
        onOpened.Invoke();
        ApplyLootedLook();
        StartCoroutine(CoinBurst());

        // 2. lid pops open with an overshoot, crate squashes and springs back
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, openTime))
        {
            float k = Mathf.Clamp01(t);
            float e = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);
            if (lid) lid.localRotation = lidClosed * Quaternion.Euler(LidAngle() * e, 0f, 0f);
            float squash = Mathf.Sin(k * Mathf.PI) * 0.12f * (1f - k);
            transform.localScale = Vector3.Scale(baseScale, new Vector3(1f + squash, 1f - squash, 1f + squash));
            yield return null;
        }
        if (lid) lid.localRotation = lidClosed * Quaternion.Euler(LidAngle(), 0f, 0f);
        transform.localScale = baseScale;
    }

    IEnumerator CoinBurst()
    {
        int line = 0;
        if (showCoinAmount && currency > 0)
            StartCoroutine(RewardPopup(string.Format(coinPopupFormat, currency), coinAmountColor, line++));
        if (showItemAmounts)
        {
            if (healingItem != null && healingItems > 0)
                StartCoroutine(RewardPopup(string.Format(medsPopupFormat, healingItems), medsColor, line++));
            foreach (ItemReward extra in extraItems)
                if (extra.item != null && extra.amount > 0)
                    StartCoroutine(extra.item.isHealthPotion
                        ? RewardPopup(string.Format(medsPopupFormat, extra.amount), medsColor, line++)
                        : RewardPopup(string.Format(itemAmountFormat, extra.amount, extra.item.itemName), otherItemColor, line++));
        }

        var coins = new List<(Transform t, Vector3 v)>();
        Vector3 top = transform.position + Vector3.up * 0.6f * baseScale.y;
        for (int i = 0; i < coinBurst; i++)
        {
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(c.GetComponent<Collider>());
            c.transform.localScale = new Vector3(0.12f, 0.012f, 0.12f);
            c.transform.position = top;
            c.transform.rotation = Random.rotation;
            var r = c.GetComponent<Renderer>();
            r.sharedMaterial = CoinMaterial(r.sharedMaterial);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Vector2 d = Random.insideUnitCircle * 1.6f;
            coins.Add((c.transform, new Vector3(d.x, Random.Range(3f, 4.5f), d.y)));
        }

        // pop up and fall a little...
        float t = 0f;
        while (t < 0.45f)
        {
            t += Time.deltaTime;
            for (int i = 0; i < coins.Count; i++)
            {
                var (tr, v) = coins[i];
                v += Vector3.down * 12f * Time.deltaTime;
                tr.position += v * Time.deltaTime;
                tr.Rotate(600f * Time.deltaTime, 0f, 300f * Time.deltaTime);
                coins[i] = (tr, v);
            }
            yield return null;
        }

        // ...then fly into the player
        Vector3[] start = new Vector3[coins.Count];
        for (int i = 0; i < coins.Count; i++) start[i] = coins[i].t.position;
        for (t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            Vector3 goal = player ? player.position + Vector3.up * 0.8f : top;
            float e = t * t;
            for (int i = 0; i < coins.Count; i++)
            {
                coins[i].t.position = Vector3.Lerp(start[i], goal, e);
                coins[i].t.localScale = Vector3.Lerp(new Vector3(0.12f, 0.012f, 0.12f), Vector3.zero, e * e);
            }
            yield return null;
        }
        foreach (var c in coins) Destroy(c.t.gameObject);

        if (coinSound && audioSource) audioSource.PlayOneShot(coinSound);
        GiveRewards();
    }

    // "+25" / "+1 Health Potion" that pops up above the crate, floats up and fades out.
    // line 0 is the top line; later lines appear a moment later, just below it.
    IEnumerator RewardPopup(string message, Color color, int line)
    {
        if (line > 0) yield return new WaitForSeconds(popupLineDelay * line);

        var go = new GameObject("RewardPopup_" + name);
        var text = go.AddComponent<TextMeshPro>();
        TMP_FontAsset f = promptFont;
        if (f == null && bubble != null)
        {
            TMP_Text bubbleText = bubble.GetComponentInChildren<TMP_Text>(true);
            if (bubbleText) f = bubbleText.font;
        }
        if (f) text.font = f;
        text.text = message;
        text.fontSize = line == 0 ? coinAmountSize : coinAmountSize * 0.75f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = color;
        text.outlineWidth = 0.2f;
        text.outlineColor = new Color32(12, 10, 16, 255);
        var r = text.GetComponent<Renderer>();
        if (r) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }

        Vector3 start = transform.position + Vector3.up * (0.9f * baseScale.y + 0.35f - line * coinAmountSize * 0.11f);
        for (float t = 0f; t < coinAmountTime; t += Time.deltaTime)
        {
            float k = t / coinAmountTime;
            // pop in with a little overshoot, drift up, fade out at the end
            float pop = k < 0.15f ? Mathf.Lerp(0.3f, 1.2f, k / 0.15f) : k < 0.28f ? Mathf.Lerp(1.2f, 1f, (k - 0.15f) / 0.13f) : 1f;
            go.transform.localScale = Vector3.one * pop;
            go.transform.position = start + Vector3.up * (0.7f * (1f - (1f - k) * (1f - k)));
            if (Camera.main) go.transform.rotation = Camera.main.transform.rotation;
            text.alpha = k < 0.65f ? 1f : 1f - Mathf.InverseLerp(0.65f, 1f, k);
            yield return null;
        }
        Destroy(go);
    }

    void GiveRewards()
    {
        // Include inactive objects: the inventory UI is usually switched off until you press I.
        Inventory inventory = inventoryOverride ? inventoryOverride : FindAnyObjectByType<Inventory>(FindObjectsInactive.Include);
        if (healingItem == null && healingItems > 0)
            Debug.LogWarning($"[LootCrate] {name}: Healing Item is empty in the Inspector, so no healing item was given. Drag your health potion ItemSO into it.", this);
        if (inventory == null)
            Debug.LogWarning($"[LootCrate] {name}: no Inventory found in the scene, items not given.", this);
        else
        {
            if (healingItem != null && healingItems > 0) inventory.AddItem(healingItem, healingItems);
            foreach (ItemReward r in extraItems)
                if (r.item != null && r.amount > 0) inventory.AddItem(r.item, r.amount);
        }

        if (currency > 0)
        {
            if (PlayerStats.Instance != null) PlayerStats.Instance.AddMoney(currency);
            else Debug.LogWarning($"[LootCrate] {name}: no PlayerStats found, money not given.", this);
            onGiveCurrency.Invoke(currency);
        }
        Debug.Log($"[LootCrate] {name}: gave {healingItems} x {(healingItem ? healingItem.itemName : "(no healing item set)")} and {currency} currency (money now {(PlayerStats.Instance ? PlayerStats.Instance.Money : 0)}).", this);
    }

    void ApplyLootedLook()
    {
        if (sparkles) sparkles.gameObject.SetActive(false);
        if (dimWhenLooted == null || dimWhenLooted.Length == 0) return;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.sharedMaterials; bool changed = false;
            for (int i = 0; i < mats.Length; i++)
                foreach (MaterialSwap s in dimWhenLooted)
                    if (s.glowing && s.dim && mats[i] == s.glowing) { mats[i] = s.dim; changed = true; }
            if (changed) r.sharedMaterials = mats;
        }
    }

    Material CoinMaterial(Material template)
    {
        if (coinMat == null)
        {
            coinMat = new Material(template);
            coinMat.color = coinColor;
            if (coinMat.HasProperty("_EmissionColor")) { coinMat.EnableKeyword("_EMISSION"); coinMat.SetColor("_EmissionColor", coinColor * 0.6f); }
        }
        return coinMat;
    }

    string ActionText() => promptAction.Replace("{key}", $"<color=#FFC83D>{interactKey.ToString().ToUpper()}</color>");

    GameObject UsePromptPrefab()
    {
        // Its own object (not a child of the crate), so the crate's scale and squash don't stretch the bubble.
        GameObject go = promptPrefab.scene.IsValid() ? promptPrefab : Instantiate(promptPrefab);   // scene object or prefab asset
        go.name = "LootPrompt_" + name;
        go.transform.SetParent(null, false);
        go.transform.position = transform.position + Vector3.up * promptHeight;
        go.transform.localScale = Vector3.one * promptScale;
        ownsPrompt = !promptPrefab.scene.IsValid();

        bubble = go.GetComponent<SpeechBubble3D>();
        if (bubble != null)
        {
            // Same as the NPCs: name line + prompt line, with the key in the bubble's own highlight colour.
            string action = promptAction.Replace("{key}", bubble.HighlightKey(interactKey.ToString().ToUpper()));
            bubble.SetContent(promptTitle, action);
        }
        else
        {
            // Any other prefab: first text = title, second = action.
            prefabTmp = go.GetComponentsInChildren<TMP_Text>(true);
            prefabText = go.GetComponentsInChildren<Text>(true);
            ApplyPromptTexts();
        }
        return go;
    }

    SpeechBubble3D bubble;
    bool ownsPrompt;

    // For prefabs that aren't SpeechBubble3D: first text = title, second = action.
    TMP_Text[] prefabTmp;
    Text[] prefabText;
    void ApplyPromptTexts()
    {
        string action = ActionText();
        if (prefabTmp != null && prefabTmp.Length > 0)
        {
            if (prefabTmp[0].text != promptTitle) prefabTmp[0].text = promptTitle;
            if (prefabTmp.Length > 1 && prefabTmp[1].text != action) { prefabTmp[1].richText = true; prefabTmp[1].text = action; }
        }
        else if (prefabText != null && prefabText.Length > 0)
        {
            string plainAction = promptAction.Replace("{key}", interactKey.ToString().ToUpper());
            if (prefabText[0].text != promptTitle) prefabText[0].text = promptTitle;
            if (prefabText.Length > 1 && prefabText[1].text != plainAction) prefabText[1].text = plainAction;
        }
    }

    bool screenPrompt;
    static Canvas promptCanvas;

    static Canvas PromptCanvas()
    {
        if (promptCanvas) return promptCanvas;
        var go = new GameObject("LootPromptCanvas", typeof(Canvas), typeof(CanvasScaler));
        promptCanvas = go.GetComponent<Canvas>();
        promptCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        promptCanvas.sortingOrder = 50;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return promptCanvas;
    }

    /// Built-in prompt drawn on the screen above the crate, styled like the NPC bubbles:
    /// dark rounded panel with a tail, cream title, smaller action line with a yellow key.
    GameObject MakePrompt()
    {
        screenPrompt = true;
        var root = new GameObject("LootPrompt_" + name, typeof(RectTransform));
        root.transform.SetParent(PromptCanvas().transform, false);
        var rt = (RectTransform)root.transform;
        bool hasTitle = !string.IsNullOrEmpty(promptTitle);
        float f = promptFontSize;
        float w = f * 9f, h = hasTitle ? f * 2.35f : f * 1.5f, tailSize = f * 0.55f;
        rt.sizeDelta = new Vector2(w, h);
        rt.pivot = new Vector2(0.5f, 0f);
        Color panelColor = new Color(0.13f, 0.11f, 0.14f, 0.93f);

        if (promptBackground)
        {
            Sprite rounded = null;
            try { rounded = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd"); } catch { }

            // tail: a small rotated square poking out under the middle of the panel, pointing at the crate
            var tail = new GameObject("Tail", typeof(RectTransform), typeof(Image));
            tail.transform.SetParent(root.transform, false);
            var trt = (RectTransform)tail.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
            trt.sizeDelta = new Vector2(tailSize, tailSize);
            trt.anchoredPosition = new Vector2(0f, 0f);
            trt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var timg = tail.GetComponent<Image>(); timg.color = panelColor; timg.raycastTarget = false;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(0f, tailSize * 0.45f); prt.offsetMax = Vector2.zero;   // leave room for the tail
            var img = panel.GetComponent<Image>();
            img.color = panelColor; img.raycastTarget = false;
            if (rounded) { img.sprite = rounded; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.22f; }
        }

        Color cream = new Color(0.96f, 0.92f, 0.82f);
        float mid = promptBackground ? tailSize * 0.22f : 0f;
        if (hasTitle)
        {
            MakeText(root.transform, "Title", promptTitle, f, cream, new Vector2(0f, mid + f * 0.42f), FontStyles.Bold);
            MakeText(root.transform, "Action", ActionText(), f * 0.55f, new Color(0.88f, 0.84f, 0.76f), new Vector2(0f, mid - f * 0.42f), FontStyles.Bold);
        }
        else
        {
            MakeText(root.transform, "Action", ActionText(), f * 0.75f, cream, new Vector2(0f, mid), FontStyles.Bold);
        }
        return root;
    }

    void MakeText(Transform parent, string name, string text, float size, Color color, Vector2 pos, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(size * 12f, size * 1.5f); rt.anchoredPosition = pos;
        var t = go.GetComponent<TextMeshProUGUI>();
        if (promptFont) t.font = promptFont;
        t.text = text; t.fontSize = size; t.color = color; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center; t.richText = true; t.raycastTarget = false;
        t.characterSpacing = 2f;
        t.outlineWidth = promptBackground ? 0.08f : 0.22f;   // dark outline so it reads on any background
        t.outlineColor = new Color32(15, 12, 20, 255);
    }

    void OnDisable()
    {
        if (bubble) bubble.HideImmediate();
        else if (prompt) prompt.SetActive(false);
    }

    void OnDestroy()
    {
        if (coinMat) Destroy(coinMat);
        if ((screenPrompt || ownsPrompt) && prompt) Destroy(prompt);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}