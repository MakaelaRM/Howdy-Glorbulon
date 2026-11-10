using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class SceneTransition : MonoBehaviour
{
    [SerializeField] private string sceneToLoad = "Mine";

    [Header("Spawn point (off = load the scene as it is)")]
    [Tooltip("On: after the scene loads, the player is moved to the object named below. Off: the player starts wherever they are placed in that scene.")]
    [SerializeField] private bool pickSpawnPoint = false;
    [Tooltip("Name of an empty GameObject in the scene you are loading, e.g. SpawnPoint_FromTown.")]
    [SerializeField] private string spawnPointName = "";
    [Tooltip("Also turn the player to match the spawn point's rotation.")]
    [SerializeField] private bool useSpawnRotation = true;

    [Header("Transition animation")]
    [Tooltip("Play the animation. Off = load instantly like before.")]
    [SerializeField] private bool animate = true;
    [Tooltip("Which animation to play. You can change it during Play and walk through again to compare.")]
    [SerializeField] private TransitionStyle style = TransitionStyle.Blinds;
    [Tooltip("Shown in the middle of the black screen while loading, e.g. THE MINE. Leave empty for none.")]
    [SerializeField] private string destinationTitle = "";
    [Tooltip("Small line under the title, e.g. Level 1. Leave empty for none.")]
    [SerializeField] private string destinationSubtitle = "";
    [Tooltip("The font your speech bubbles use (Kleader).")]
    [SerializeField] private TMP_FontAsset titleFont;
    [Tooltip("Seconds for the circle to close in on the player.")]
    [SerializeField] private float closeTime = 0.55f;
    [Tooltip("Seconds for the circle to open in the new scene.")]
    [SerializeField] private float openTime = 0.6f;
    [Tooltip("Minimum seconds on the black screen (so the title can be read).")]
    [SerializeField] private float blackTime = 0.6f;
    [SerializeField] private Color rimColor = new Color(0.92f, 0.84f, 0.68f);
    [SerializeField] private Color titleColor = new Color(0.98f, 0.95f, 0.88f);
    [SerializeField] private Color subtitleColor = new Color(0.96f, 0.78f, 0.42f);

    // Remembered across the scene load.
    static string pendingSpawnName;
    static bool pendingUseRotation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        pendingSpawnName = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (SceneTransitionOverlay.Busy) return;   // already changing scene

            pendingSpawnName = (pickSpawnPoint && !string.IsNullOrEmpty(spawnPointName)) ? spawnPointName : null;
            pendingUseRotation = useSpawnRotation;

            if (!animate)
            {
                SceneManager.LoadScene(sceneToLoad);
                return;
            }

            // stop the player walking while the circle closes
            var movement = other.GetComponentInParent<CharacterMovement>();
            if (movement != null) movement.enabled = false;

            SceneTransitionOverlay.Play(new SceneTransitionOverlay.Settings
            {
                scene = sceneToLoad,
                style = style,
                title = destinationTitle,
                subtitle = destinationSubtitle,
                font = titleFont,
                closeTime = closeTime,
                openTime = openTime,
                blackTime = blackTime,
                rimColor = rimColor,
                titleColor = titleColor,
                subtitleColor = subtitleColor,
                focus = other.transform
            });
        }
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (string.IsNullOrEmpty(pendingSpawnName)) return;

        string wanted = pendingSpawnName;
        pendingSpawnName = null; // only use it once

        var spawn = GameObject.Find(wanted);
        var player = GameObject.FindGameObjectWithTag("Player");

        if (spawn == null)
        {
            Debug.LogWarning("[SceneTransition] No object named '" + wanted + "' in scene '" + scene.name + "'. Player left where the scene put them.");
            return;
        }
        if (player == null)
        {
            Debug.LogWarning("[SceneTransition] No object tagged Player in scene '" + scene.name + "'.");
            return;
        }

        // A CharacterController would push the player back, so turn it off while moving.
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        player.transform.position = spawn.transform.position;
        if (pendingUseRotation) player.transform.rotation = spawn.transform.rotation;

        var rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = spawn.transform.position;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (cc != null) cc.enabled = true;
    }
}

/// The scene transition animations to pick from.
public enum TransitionStyle
{
    [Tooltip("Classic cartoon: a black circle closes in on the player.")]
    Iris,
    [Tooltip("Classic: simple fade to black and back.")]
    Fade,
    [Tooltip("Classic: a slanted black wipe sweeps across the screen.")]
    Wipe,
    [Tooltip("Classic: horizontal blinds close and open.")]
    Blinds,
    [Tooltip("Western: two wooden saloon doors swing shut, then open in the new scene.")]
    SaloonDoors,
    [Tooltip("Western: a WANTED-style poster with the destination slams onto the screen.")]
    WantedPoster,
    [Tooltip("Space: stars stretch into warp-speed streaks, then a flash.")]
    WarpSpeed,
    [Tooltip("Space: a teleporter beam swallows the player and beams them into the new scene.")]
    Teleport
}

/// Plays the transition: covers the screen, shows the destination title, loads the scene, uncovers it.
/// Created automatically and survives the scene load. Everything is drawn with UI images made in code,
/// so there are no extra assets to set up.
class SceneTransitionOverlay : MonoBehaviour
{
    public struct Settings
    {
        public string scene, title, subtitle;
        public TransitionStyle style;
        public TMP_FontAsset font;
        public float closeTime, openTime, blackTime;
        public Color rimColor, titleColor, subtitleColor;
        public Transform focus;
    }

    public static bool Busy { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Busy = false; }

    Settings s;
    float W, H;
    readonly System.Collections.Generic.List<Object> made = new System.Collections.Generic.List<Object>();

    // shared
    Image black;                         // full-screen black
    CanvasGroup textGroup;
    // iris
    RectTransform hole; Image[] sides;
    // wipe
    RectTransform wipe;
    // blinds
    RectTransform[] blinds;
    // saloon doors
    RectTransform doorL, doorR;
    // wanted poster
    RectTransform poster;
    // warp
    RectTransform[] streaks; Vector2[] streakDir; float[] streakSpeed; Image flash;
    // teleport
    RectTransform beam; Image beamImg;

    static readonly Color Wood = new Color(0.36f, 0.22f, 0.12f);
    static readonly Color Parchment = new Color(0.87f, 0.77f, 0.57f);
    static readonly Color Ink = new Color(0.22f, 0.13f, 0.07f);
    static readonly Color SpaceDark = new Color(0.02f, 0.02f, 0.07f);
    static readonly Color BeamColor = new Color(0.35f, 0.95f, 1f);

    public static void Play(Settings settings)
    {
        Busy = true;
        var go = new GameObject("SceneTransition");
        DontDestroyOnLoad(go);
        var o = go.AddComponent<SceneTransitionOverlay>();
        o.s = settings;
        if (string.IsNullOrEmpty(o.s.title) && settings.style == TransitionStyle.WantedPoster) o.s.title = settings.scene.ToUpper();
        o.Build();
        o.StartCoroutine(o.Run());
    }

    // ================================================================== building

    void Build()
    {
        W = Screen.width; H = Screen.height;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;   // above every HUD

        switch (s.style)
        {
            case TransitionStyle.Iris:
                sides = new Image[4];
                for (int i = 0; i < 4; i++) sides[i] = Img("Black" + i, null, Color.black);
                hole = Img("Iris", Spr(HoleTexture(512, s.rimColor)), Color.white).rectTransform;
                break;

            case TransitionStyle.Wipe:
                wipe = Img("Wipe", null, Color.black).rectTransform;
                wipe.sizeDelta = new Vector2(W * 1.5f + H, H * 3f);
                wipe.localRotation = Quaternion.Euler(0f, 0f, -14f);
                var edge = Img("Edge", null, s.rimColor, wipe).rectTransform;   // cream leading edge
                edge.anchorMin = new Vector2(1f, 0f); edge.anchorMax = Vector2.one; edge.pivot = new Vector2(0f, 0.5f);
                edge.anchoredPosition = Vector2.zero; edge.sizeDelta = new Vector2(H * 0.012f, 0f);
                break;

            case TransitionStyle.Blinds:
                int n = 9;
                blinds = new RectTransform[n];
                for (int i = 0; i < n; i++)
                {
                    var b = Img("Blind" + i, null, Color.black).rectTransform;
                    b.pivot = new Vector2(0.5f, i % 2 == 0 ? 1f : 0f);   // alternate grow direction
                    b.anchoredPosition = new Vector2(W * 0.5f, H * (i + (i % 2 == 0 ? 1f : 0f)) / n);
                    b.sizeDelta = new Vector2(W + 4f, 0f);
                    blinds[i] = b;
                }
                break;

            case TransitionStyle.SaloonDoors:
                black = Img("Black", null, Color.black); Full(black.rectTransform);
                Sprite door = Spr(DoorTexture(160, 320));
                doorL = Img("DoorLeft", door, Color.white).rectTransform;
                doorR = Img("DoorRight", door, Color.white).rectTransform;
                doorR.localScale = new Vector3(-1f, 1f, 1f);   // mirrored
                foreach (var d in new[] { doorL, doorR })
                {
                    d.sizeDelta = new Vector2(W * 0.5f + 6f, H + 8f);
                    d.pivot = new Vector2(0f, 0.5f);
                }
                break;

            case TransitionStyle.WantedPoster:
                black = Img("Black", null, Color.black); Full(black.rectTransform);
                poster = Img("Poster", Spr(ParchmentTexture(256, 340)), Color.white).rectTransform;
                float ph = H * 0.72f;
                poster.sizeDelta = new Vector2(ph * 256f / 340f, ph);
                PosterText("Top", "NOW ENTERING", ph * 0.07f, Ink, ph * 0.33f, 4f);
                var line = Img("Line", null, Ink, poster).rectTransform;
                line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f); line.pivot = new Vector2(0.5f, 0.5f);
                line.sizeDelta = new Vector2(ph * 0.55f, ph * 0.008f); line.anchoredPosition = new Vector2(0f, ph * 0.24f);
                var big = PosterText("Title", s.title, ph * 0.13f, Ink, ph * 0.05f, 6f);
                big.enableAutoSizing = true; big.fontSizeMax = ph * 0.13f; big.fontSizeMin = ph * 0.05f;
                big.rectTransform.sizeDelta = new Vector2(ph * 0.62f, ph * 0.3f);
                big.textWrappingMode = TextWrappingModes.Normal;
                PosterText("Sub", s.subtitle, ph * 0.06f, new Color(0.55f, 0.16f, 0.1f), -ph * 0.2f, 4f);
                PosterText("Bottom", "~ DEAD OR ALIVE ~", ph * 0.045f, Ink, -ph * 0.33f, 3f);
                break;

            case TransitionStyle.WarpSpeed:
                black = Img("Space", null, SpaceDark); Full(black.rectTransform);
                int count = 90;
                streaks = new RectTransform[count]; streakDir = new Vector2[count]; streakSpeed = new float[count];
                Sprite streak = Spr(StreakTexture());
                for (int i = 0; i < count; i++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    streakDir[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    streakSpeed[i] = Random.Range(0.25f, 1f);
                    Color c = Color.Lerp(Color.white, new Color(0.6f, 0.85f, 1f), Random.value);
                    var r = Img("Streak" + i, streak, c).rectTransform;
                    r.pivot = new Vector2(0f, 0.5f);
                    r.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
                    streaks[i] = r;
                }
                flash = Img("Flash", null, Color.white); Full(flash.rectTransform);
                break;

            case TransitionStyle.Teleport:
                beamImg = Img("Beam", Spr(BeamTexture()), BeamColor);
                beam = beamImg.rectTransform;
                beam.pivot = new Vector2(0.5f, 0.5f);
                black = Img("Black", null, Color.black); Full(black.rectTransform);
                break;

            default: // Fade
                black = Img("Black", null, Color.black); Full(black.rectTransform);
                break;
        }

        // title on the black screen (the poster has its own)
        var textRoot = new GameObject("Text", typeof(RectTransform), typeof(CanvasGroup));
        textRoot.transform.SetParent(transform, false);
        Full((RectTransform)textRoot.transform);
        textGroup = textRoot.GetComponent<CanvasGroup>();
        textGroup.alpha = 0f;
        if (s.style != TransitionStyle.WantedPoster)
        {
            Txt(textRoot.transform, "Title", s.title, H * 0.075f, s.titleColor, H * 0.03f, 8f);
            Txt(textRoot.transform, "Subtitle", s.subtitle, H * 0.032f, s.subtitleColor, -H * 0.045f, 6f);
        }
    }

    // ================================================================== running

    IEnumerator Run()
    {
        // 1. cover the screen
        if (s.style == TransitionStyle.Iris) yield return IrisClose();
        else yield return Animate(0f, 1f, s.closeTime, true);

        // 2. title + load the scene
        float shownAt = Time.unscaledTime;
        bool hasText = s.style != TransitionStyle.WantedPoster && (!string.IsNullOrEmpty(s.title) || !string.IsNullOrEmpty(s.subtitle));
        if (hasText) yield return FadeText(0f, 1f, 0.25f);

        AsyncOperation load = SceneManager.LoadSceneAsync(s.scene);
        if (load == null)
        {
            Debug.LogError("[SceneTransition] Couldn't load scene '" + s.scene + "'. Is it in File > Build Profiles > Scene List?");
            Destroy(gameObject);
            yield break;
        }
        while (!load.isDone) { Hold(); yield return null; }

        s.focus = null;
        for (int i = 0; i < 3; i++) { Hold(); yield return null; }   // let the new camera settle
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player) s.focus = player.transform;

        while (Time.unscaledTime - shownAt < s.blackTime) { Hold(); yield return null; }
        if (hasText) yield return FadeText(1f, 0f, 0.2f);

        // 3. uncover
        if (s.style == TransitionStyle.Iris) yield return IrisOpen();
        else yield return Animate(1f, 0f, s.openTime, false);

        Destroy(gameObject);
    }

    void Hold()
    {
        if (s.style == TransitionStyle.Iris) SetIris(0f);
        else Cover(1f, true);
    }

    IEnumerator Animate(float from, float to, float time, bool closing)
    {
        time = Mathf.Max(0.01f, time);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / time)
        {
            Cover(Mathf.Lerp(from, to, Mathf.Clamp01(t)), closing);
            yield return null;
        }
        Cover(to, closing);
    }

    IEnumerator FadeText(float from, float to, float time)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / time)
        {
            textGroup.alpha = Mathf.Lerp(from, to, t);
            Hold();
            yield return null;
        }
        textGroup.alpha = to;
    }

    void OnDestroy()
    {
        foreach (Object o in made) if (o) Destroy(o);
        Busy = false;
    }

    /// p: 0 = screen clear, 1 = fully covered. closing: true on the way in, false on the way out.
    void Cover(float p, bool closing)
    {
        switch (s.style)
        {
            case TransitionStyle.Fade:
                black.color = new Color(0f, 0f, 0f, EaseInOut(p));
                break;

            case TransitionStyle.Wipe:
            {
                // sweeps left -> right: comes in from the left, then carries on off to the right
                float span = wipe.sizeDelta.x;
                float offLeft = -span * 0.5f - H * 0.4f;
                float offRight = W + span * 0.5f + H * 0.4f;
                float x = Mathf.Lerp(closing ? offLeft : offRight, W * 0.5f, EaseInOut(p));
                wipe.anchoredPosition = new Vector2(x, H * 0.5f);
                break;
            }

            case TransitionStyle.Blinds:
                for (int i = 0; i < blinds.Length; i++)
                {
                    float delay = i / (float)(blinds.Length - 1) * 0.4f;
                    float k = Mathf.Clamp01((p - (closing ? delay : 0.4f - delay)) / 0.6f);
                    blinds[i].sizeDelta = new Vector2(W + 4f, (H / blinds.Length + 2f) * EaseInOut(k));
                }
                break;

            case TransitionStyle.SaloonDoors:
            {
                // closing: slam shut with a wobble. opening: swing out quickly.
                float k = closing ? EaseOutBack(p, 2.4f) : EaseInOut(p);
                float half = W * 0.5f + 6f;
                doorL.anchoredPosition = new Vector2(Mathf.Lerp(-half, 0f, k), H * 0.5f);
                doorR.anchoredPosition = new Vector2(Mathf.Lerp(W + half, W, k), H * 0.5f);
                black.color = new Color(0f, 0f, 0f, Mathf.Clamp01((p - 0.85f) / 0.15f));
                break;
            }

            case TransitionStyle.WantedPoster:
            {
                black.color = new Color(0f, 0f, 0f, Mathf.Clamp01(p * 1.4f));
                float k = Mathf.Clamp01((p - 0.25f) / 0.75f);
                if (closing)
                {
                    // slams down from above, a bit tilted, with a thud
                    float e = EaseOutBack(k, 2f);
                    poster.anchoredPosition = new Vector2(W * 0.5f, Mathf.Lerp(H * 1.6f, H * 0.5f, e));
                    poster.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-14f, -3f, e));
                    float thud = k > 0.8f ? Mathf.Sin((k - 0.8f) / 0.2f * Mathf.PI) * 0.04f : 0f;
                    poster.localScale = Vector3.one * (1f + thud);
                }
                else
                {
                    // flies off upward
                    poster.anchoredPosition = new Vector2(W * 0.5f, Mathf.Lerp(H * 1.7f, H * 0.5f, EaseInOut(k)));
                    poster.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(10f, -3f, k));
                    poster.localScale = Vector3.one;
                }
                break;
            }

            case TransitionStyle.WarpSpeed:
            {
                Vector2 c = new Vector2(W * 0.5f, H * 0.5f);
                float diag = Mathf.Sqrt(W * W + H * H) * 0.5f;
                black.color = new Color(SpaceDark.r, SpaceDark.g, SpaceDark.b, Mathf.Clamp01(p * 2f));
                float stretch = Mathf.Pow(Mathf.Clamp01(p / 0.85f), 2.2f);
                for (int i = 0; i < streaks.Length; i++)
                {
                    float sp = streakSpeed[i];
                    float start = diag * (0.05f + 0.6f * sp * stretch);
                    float len = Mathf.Lerp(2f, diag * 1.2f * sp, stretch);
                    streaks[i].anchoredPosition = c + streakDir[i] * start;
                    streaks[i].sizeDelta = new Vector2(len, Mathf.Lerp(2f, 5f, sp));
                    streaks[i].GetComponent<Image>().canvasRenderer.SetAlpha(Mathf.Clamp01(p * 3f) * (p > 0.92f ? 1f - (p - 0.92f) / 0.08f : 1f));
                }
                // white flash at the jump, then black
                float f = p < 0.82f ? 0f : p < 0.92f ? (p - 0.82f) / 0.1f : 1f - (p - 0.92f) / 0.08f;
                flash.color = new Color(1f, 1f, 1f, Mathf.Clamp01(f));
                if (p >= 0.999f) black.color = Color.black;
                break;
            }

            case TransitionStyle.Teleport:
            {
                Vector2 c = FocusPoint();
                float k = Mathf.Clamp01(p / 0.75f);
                float e = k * k * (3f - 2f * k);
                // thin column on the player that widens until the screen is all light
                float width = Mathf.Lerp(H * 0.02f, W * 2.6f, Mathf.Pow(e, 2.2f));
                beam.anchoredPosition = new Vector2(c.x, H * 0.5f);
                beam.sizeDelta = new Vector2(width * (1f + 0.04f * Mathf.Sin(Time.unscaledTime * 40f)), H + 10f);
                beamImg.color = Color.Lerp(new Color(BeamColor.r, BeamColor.g, BeamColor.b, Mathf.Clamp01(k * 4f)), Color.white, Mathf.Clamp01((e - 0.5f) * 2f));
                black.color = new Color(0f, 0f, 0f, Mathf.Clamp01((p - 0.75f) / 0.25f));
                break;
            }
        }
    }

    // ---------------------------------------------------------------- iris (staged: close, pause on player, snap)

    IEnumerator IrisClose()
    {
        float open = IrisOpenSize(), small = H * 0.24f;
        yield return IrisAnim(open, small, s.closeTime, EaseInOut);
        yield return IrisWait(0.14f, small);
        yield return IrisAnim(small, 0f, 0.16f, t => EaseInBackCurve(t));
    }

    IEnumerator IrisOpen()
    {
        float open = IrisOpenSize(), small = H * 0.24f;
        yield return IrisAnim(0f, small, 0.18f, t => EaseOutBack(t, 1.70158f));
        yield return IrisWait(0.12f, small);
        yield return IrisAnim(small, open, s.openTime, EaseInOut);
    }

    float irisSize;
    IEnumerator IrisAnim(float from, float to, float time, System.Func<float, float> ease)
    {
        time = Mathf.Max(0.01f, time);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / time)
        {
            SetIris(Mathf.LerpUnclamped(from, to, ease(Mathf.Clamp01(t))));
            yield return null;
        }
        SetIris(to);
    }

    IEnumerator IrisWait(float time, float size)
    {
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime) { SetIris(size); yield return null; }
    }

    void SetIris(float diameter)
    {
        irisSize = Mathf.Max(0f, diameter);
        Vector2 c = FocusPoint();
        float r = irisSize * 0.5f;
        hole.gameObject.SetActive(irisSize > 0.5f);
        hole.anchoredPosition = c;
        hole.sizeDelta = new Vector2(irisSize, irisSize);

        float left = c.x - r, right = c.x + r, bottom = c.y - r, top = c.y + r;
        if (irisSize <= 0.5f) { left = right = c.x; bottom = top = c.y; }
        SetRect(sides[0], Mathf.Min(0f, left) - 2f, -2f, left + 1f, H + 2f);
        SetRect(sides[1], right - 1f, -2f, Mathf.Max(W, right) + 2f, H + 2f);
        SetRect(sides[2], left, top - 1f, right, Mathf.Max(H, top) + 2f);
        SetRect(sides[3], left, Mathf.Min(0f, bottom) - 2f, right, bottom + 1f);
    }

    float IrisOpenSize()
    {
        Vector2 c = FocusPoint();
        float far = 0f;
        foreach (Vector2 corner in new[] { Vector2.zero, new Vector2(W, 0f), new Vector2(0f, H), new Vector2(W, H) })
            far = Mathf.Max(far, Vector2.Distance(c, corner));
        return far * 2f / 0.9f + 20f;
    }

    // ================================================================== helpers

    Vector2 FocusPoint()
    {
        Camera cam = Camera.main;
        if (s.focus && cam)
        {
            Vector3 p = cam.WorldToScreenPoint(s.focus.position + Vector3.up * 0.9f);
            if (p.z > 0f) return new Vector2(Mathf.Clamp(p.x, 0f, W), Mathf.Clamp(p.y, 0f, H));
        }
        return new Vector2(W * 0.5f, H * 0.5f);
    }

    Image Img(string name, Sprite sprite, Color color, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent ? parent : transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-99999f, 0f);   // off screen until placed
        var img = go.GetComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return img;
    }

    static void Full(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-2f, -2f); rt.offsetMax = new Vector2(2f, 2f);
    }

    static void SetRect(Image img, float x0, float y0, float x1, float y1)
    {
        var rt = img.rectTransform;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x0, y0);
        rt.sizeDelta = new Vector2(Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
    }

    TextMeshProUGUI Txt(Transform parent, string name, string text, float size, Color color, float y, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (s.font) t.font = s.font;
        t.text = text ?? "";
        t.fontSize = Mathf.Max(1f, size);
        t.color = color;
        t.characterSpacing = spacing;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(W, Mathf.Max(1f, size) * 1.6f);
        rt.anchoredPosition = new Vector2(0f, y);
        return t;
    }

    TextMeshProUGUI PosterText(string name, string text, float size, Color color, float y, float spacing)
    {
        var t = Txt(poster, name, text, size, color, y, spacing);
        t.rectTransform.sizeDelta = new Vector2(poster.sizeDelta.x * 0.85f, Mathf.Max(1f, size) * 1.6f);
        return t;
    }

    Sprite Spr(Texture2D tex)
    {
        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        made.Add(tex); made.Add(sp);
        return sp;
    }

    static Texture2D NewTex(int w, int h, string name) =>
        new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };

    // Black square with a see-through circle and a cream ring.
    static Texture2D HoleTexture(int size, Color rim)
    {
        var tex = NewTex(size, size, "IrisHole");
        var px = new Color[size * size];
        float half = size * 0.5f, holeR = size * 0.45f, ringR = size * 0.475f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
            float inHole = Mathf.Clamp01(holeR - d + 0.5f);
            float inRing = Mathf.Clamp01(ringR - d + 0.5f);
            float shade = Mathf.Lerp(0.75f, 1.05f, Mathf.Sin(Mathf.Clamp01((d - holeR) / (ringR - holeR)) * Mathf.PI));
            Color ring = rim * shade; ring.a = 1f;
            Color c = Color.Lerp(Color.black, ring, inRing);
            c.a = 1f - inHole;
            px[y * size + x] = c;
        }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    // Wooden saloon door: planks, a frame, and a cream trim on the inner edge.
    static Texture2D DoorTexture(int w, int h)
    {
        var tex = NewTex(w, h, "SaloonDoor");
        var px = new Color[w * h];
        int planks = 5;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float u = x / (float)w, v = y / (float)h;
            float grain = Mathf.PerlinNoise(u * 6f + (int)(u * planks) * 3.1f, v * 40f) * 0.25f;
            Color c = Wood * (0.85f + grain);
            if (Mathf.Repeat(u * planks, 1f) < 0.06f) c *= 0.55f;                       // gaps between planks
            bool frame = u < 0.06f || v < 0.05f || v > 0.95f || Mathf.Abs(v - 0.5f) < 0.025f || Mathf.Abs(v - 0.78f) < 0.02f || Mathf.Abs(v - 0.22f) < 0.02f;
            if (frame) c = Wood * 0.7f;
            if (u > 0.94f) c = new Color(0.92f, 0.84f, 0.68f) * Mathf.Lerp(0.8f, 1.05f, Mathf.Sin((u - 0.94f) / 0.06f * Mathf.PI)); // cream trim
            c.a = 1f;
            px[y * w + x] = c;
        }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    // Old paper with burnt, darker edges.
    static Texture2D ParchmentTexture(int w, int h)
    {
        var tex = NewTex(w, h, "Parchment");
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float u = x / (float)w, v = y / (float)h;
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            float noise = Mathf.PerlinNoise(u * 8f, v * 8f) * 0.15f + Mathf.PerlinNoise(u * 40f, v * 40f) * 0.06f;
            float burn = 1f - Mathf.Clamp01((edge - 0.01f) / 0.09f) * 1f;
            Color c = Parchment * (0.92f + noise - burn * 0.45f);
            if (edge < 0.035f && edge > 0.025f) c *= 0.6f;                  // printed border line
            c.a = edge < 0.004f + Mathf.PerlinNoise(u * 30f, v * 30f) * 0.01f ? 0f : 1f;   // rough edge
            px[y * w + x] = c;
        }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    // Streak: bright head fading to a transparent tail (head on the right).
    static Texture2D StreakTexture()
    {
        var tex = NewTex(64, 4, "Streak");
        for (int x = 0; x < 64; x++)
        for (int y = 0; y < 4; y++)
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(x / 63f, 1.5f) * (y == 0 || y == 3 ? 0.5f : 1f)));
        tex.Apply();
        return tex;
    }

    // Beam: soft glowing edges, bright core.
    static Texture2D BeamTexture()
    {
        var tex = NewTex(64, 1, "Beam");
        for (int x = 0; x < 64; x++)
        {
            float d = Mathf.Abs(x - 31.5f) / 31.5f;
            float a = Mathf.Clamp01(1f - d * d);
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    static float EaseInBackCurve(float t) { const float c1 = 1.70158f; return (c1 + 1f) * t * t * t - c1 * t * t; }
    static float EaseOutBack(float t, float c1) { float u = t - 1f; return 1f + (c1 + 1f) * u * u * u + c1 * u * u; }
}