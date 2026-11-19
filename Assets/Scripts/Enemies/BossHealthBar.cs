using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Boss health bar at the top of the screen: the boss's name over a slim bar, in the game's cream/Kleader style.
/// QueenNibblerBoss shows and hides it by itself, so you don't need to add this to anything.
/// - Slides in when you enter the boss area and fades out when you leave.
/// - Hits drop the bar right away; a light "damage chunk" catches up a moment later.
/// - A notch marks where phase 2 starts. In phase 2 the bar turns deeper red and the frame pulses once.
/// - On defeat it drains, says DEFEATED, then fades away.
public class BossHealthBar : MonoBehaviour
{
    static BossHealthBar instance;

    // colours matching the speech bubbles
    static readonly Color Face = new Color(0.06f, 0.055f, 0.07f, 0.94f);
    static readonly Color Rim = new Color(0.92f, 0.84f, 0.68f, 1f);
    static readonly Color Gold = new Color(0.96f, 0.78f, 0.42f, 1f);
    static readonly Color TitleCol = new Color(0.98f, 0.95f, 0.88f, 1f);
    static readonly Color HintCol = new Color(0.80f, 0.76f, 0.70f, 1f);
    static readonly Color Track = new Color(0.14f, 0.09f, 0.12f, 1f);
    static readonly Color FillPhase1 = new Color(0.93f, 0.25f, 0.42f, 1f);   // Nibbler magenta-red
    static readonly Color FillPhase2 = new Color(0.78f, 0.06f, 0.10f, 1f);   // angry deep red
    static readonly Color Chunk = new Color(1f, 0.88f, 0.80f, 1f);

    QueenNibblerBoss boss;
    bool visible, defeated;
    float defeatedAt;
    bool wasEnraged;
    float pulse;

    CanvasGroup group;
    RectTransform panel, fill, chunk, marker;
    Image fillImg, trackImg;
    TextMeshProUGUI nameText;
    float shownFill = 1f, chunkFill = 1f, chunkHoldUntil;
    Sprite roundedSmall;

    const float BarWidth = 640f, BarHeight = 16f, NameSize = 28f;

    // ------------------------------------------------------------------ API used by the boss

    public static void Show(QueenNibblerBoss b)
    {
        if (!instance) Create(b);
        if (instance.boss != b) instance.Bind(b);
        instance.visible = true;
    }

    public static void Hide(QueenNibblerBoss b)
    {
        if (instance && instance.boss == b && !instance.defeated) instance.visible = false;
    }

    public static void Defeat(QueenNibblerBoss b)
    {
        if (!instance) return;
        instance.defeated = true;
        instance.defeatedAt = Time.time;
        instance.visible = true;
    }

    // ------------------------------------------------------------------ building

    static void Create(QueenNibblerBoss b)
    {
        var go = new GameObject("BossHealthBar", typeof(RectTransform));
        instance = go.AddComponent<BossHealthBar>();
        instance.Build(b);
    }

    void Build(QueenNibblerBoss b)
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;     // above the HUD, below scene transitions
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;

        roundedSmall = MakeRounded(7, 2);

        // no frame: just the name and the bar
        panel = NewRect("Panel", transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.sizeDelta = new Vector2(BarWidth, NameSize * 1.3f + BarHeight + 6f);
        panel.anchoredPosition = new Vector2(0f, -22f);

        nameText = NewText("Name", panel, b.barFont, NameSize, TitleCol, new Vector2(0f, 0f), NameSize * 1.3f);
        nameText.characterSpacing = 8f;
        nameText.outlineWidth = 0.22f;
        nameText.outlineColor = new Color32(12, 10, 16, 255);

        // slim bar: dark track with a thin cream edge, light chunk behind the fill, notch at the phase-2 line
        RectTransform track = NewRect("Track", panel);
        track.anchorMin = track.anchorMax = new Vector2(0.5f, 1f);
        track.pivot = new Vector2(0.5f, 1f);
        track.sizeDelta = new Vector2(BarWidth, BarHeight);
        track.anchoredPosition = new Vector2(0f, -(NameSize * 1.3f + 4f));
        trackImg = track.gameObject.AddComponent<Image>();
        trackImg.sprite = roundedSmall; trackImg.type = Image.Type.Sliced; trackImg.color = Color.white; trackImg.raycastTarget = false;

        RectTransform inner = NewRect("Inner", track);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(3f, 3f); inner.offsetMax = new Vector2(-3f, -3f);
        var innerImg = inner.gameObject.AddComponent<Image>(); innerImg.color = Track; innerImg.raycastTarget = false;
        inner.gameObject.AddComponent<RectMask2D>();

        chunk = Bar("Chunk", inner, Chunk, out _);
        fill = Bar("Fill", inner, FillPhase1, out fillImg);

        RectTransform shine = NewRect("Shine", fill);
        shine.anchorMin = new Vector2(0f, 0.55f); shine.anchorMax = new Vector2(1f, 0.85f);
        shine.offsetMin = shine.offsetMax = Vector2.zero;
        var shineImg = shine.gameObject.AddComponent<Image>(); shineImg.color = new Color(1f, 1f, 1f, 0.18f); shineImg.raycastTarget = false;

        marker = NewRect("Phase2Notch", track);
        marker.anchorMin = new Vector2(0f, 0f); marker.anchorMax = new Vector2(0f, 1f);
        marker.pivot = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(3f, 6f);
        var markerImg = marker.gameObject.AddComponent<Image>(); markerImg.color = Rim; markerImg.raycastTarget = false;

        Bind(b);
    }

    void Bind(QueenNibblerBoss b)
    {
        boss = b;
        defeated = false;
        wasEnraged = b.Enraged;
        if (b.barFont) nameText.font = b.barFont;
        nameText.text = b.bossName;
        nameText.color = TitleCol;
        shownFill = chunkFill = Fraction();
        marker.anchorMin = new Vector2(b.enrageAt, 0f); marker.anchorMax = new Vector2(b.enrageAt, 1f);
        marker.anchoredPosition = Vector2.zero;
        fillImg.color = b.Enraged ? FillPhase2 : FillPhase1;
        SetBar(fill, shownFill); SetBar(chunk, chunkFill);
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (!boss) { visible = false; }

        // defeated: drain, say so, then fade
        if (defeated)
        {
            float since = Time.time - defeatedAt;
            nameText.text = boss ? boss.bossName + " DEFEATED" : "DEFEATED";
            nameText.color = Gold;
            if (since > 3.5f) visible = false;
        }

        // slide in / fade
        float targetAlpha = visible ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, dt * (visible ? 3f : 2f));
        float slide = 1f - group.alpha;
        panel.anchoredPosition = new Vector2(0f, -22f + slide * 40f);
        if (!visible && group.alpha <= 0f && (defeated || !boss)) { Destroy(gameObject); return; }
        if (!boss) return;

        // fill drops fast, the chunk waits and then catches up
        float target = defeated ? 0f : Fraction();
        if (target < shownFill - 0.0001f) chunkHoldUntil = Time.time + 0.55f;
        shownFill = Mathf.MoveTowards(shownFill, target, dt * 2.5f);
        if (Time.time >= chunkHoldUntil) chunkFill = Mathf.MoveTowards(chunkFill, shownFill, dt * 0.6f);
        if (chunkFill < shownFill) chunkFill = shownFill;                // healed (fight reset)
        SetBar(fill, shownFill); SetBar(chunk, chunkFill);

        // phase 2: colour change and one pulse of the frame
        if (boss.Enraged && !wasEnraged) pulse = 1f;
        wasEnraged = boss.Enraged;
        fillImg.color = Color.Lerp(fillImg.color, boss.Enraged ? FillPhase2 : FillPhase1, dt * 4f);
        if (pulse > 0f)
        {
            pulse = Mathf.Max(0f, pulse - dt * 1.5f);
            float s = 1f + 0.06f * Mathf.Sin(pulse * Mathf.PI);
            panel.localScale = new Vector3(s, s, 1f);
            trackImg.color = Color.Lerp(Color.white, new Color(1f, 0.55f, 0.45f), Mathf.Sin(pulse * Mathf.PI));
        }
        else { panel.localScale = Vector3.one; trackImg.color = Color.white; }
    }

    float Fraction() => boss && boss.MaxHealth > 0f ? Mathf.Clamp01(boss.Health / boss.MaxHealth) : 0f;

    static void SetBar(RectTransform r, float amount)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    void OnDestroy() { if (instance == this) instance = null; }

    // ------------------------------------------------------------------ helpers

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static RectTransform Bar(string name, Transform parent, Color color, out Image img)
    {
        RectTransform r = NewRect(name, parent);
        img = r.gameObject.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        SetBar(r, 1f);
        return r;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, float size, Color color, Vector2 pos, float height)
    {
        RectTransform r = NewRect(name, parent);
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.sizeDelta = new Vector2(BarWidth + 200f, height);
        r.anchoredPosition = pos;
        var t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.fontSize = size; t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        return t;
    }

    // Dark rounded box with a cream bevelled rim (same look as the inventory and speech bubbles), 9-sliced.
    static Sprite MakeRounded(int radius, int rim)
    {
        int border = radius + 4, size = border * 2 + 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        float half = size * 0.5f, inner = half - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - (inner - radius);
                float qy = Mathf.Abs(y + 0.5f - half) - (inner - radius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float alpha = Mathf.Clamp01(0.5f - outside);
                float rimMix = Mathf.Clamp01(outside + rim + 0.5f);
                float up = y / (float)size;
                float across = Mathf.Clamp01(-outside / Mathf.Max(1f, rim));
                float bevel = Mathf.Lerp(0.72f, 1.06f, Mathf.Sin(across * Mathf.PI)) * Mathf.Lerp(0.82f, 1.08f, up);
                Color rc = Rim * bevel; rc.a = 1f;
                Color f = Color.Lerp(Face, Color.Lerp(Face, Color.white, 0.06f), up); f.a = Face.a;
                Color c = Color.Lerp(f, rc, rimMix);
                c.a *= alpha;
                px[y * size + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                             new Vector4(border, border, border, border));
    }
}