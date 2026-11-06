using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Screen-space HUD shown in dungeon scenes (wherever WeaponSwap isn't locked).
/// Create it with Tools > HUD > Create Dungeon HUD, which builds the layout and fills in these fields.
/// Health and shield come from PlayerStats; the heat ring comes from BlasterHeat.
[RequireComponent(typeof(CanvasGroup))]
public class DungeonHUD : MonoBehaviour
{
    [Header("Weapon (top left)")]
    [SerializeField] private Image weaponIcon;
    [Tooltip("The circle the icon sits in. Its pop animation plays on weapon swap.")]
    [SerializeField] private RectTransform weaponBadge;
    [SerializeField] private TextMeshProUGUI weaponNameText;
    [Tooltip("Filled (Radial 360) image with the heat gradient baked into it.")]
    [SerializeField] private Image heatFill;
    [Tooltip("Faint ring behind the heat fill.")]
    [SerializeField] private Image heatTrack;
    [Tooltip("Icons matched to weapons by name (sprite Wpn_Blaster_SixShooter goes with the Wpn_Blaster_SixShooter model).")]
    [SerializeField] private List<Sprite> weaponIcons = new List<Sprite>();
    [Tooltip("How far the icon is zoomed inside the circle. 1 = the short side just fits.")]
    [SerializeField] private float iconZoom = 1.25f;
    [Tooltip("Point of a gun's picture that sits in the middle of the circle. X: 0 = left edge, 1 = right edge. Y: 0 = bottom, 1 = top.")]
    [SerializeField] private Vector2 gunIconFocus = new Vector2(0.3f, 0.5f);
    [Tooltip("Same as Gun Icon Focus, for melee weapons.")]
    [SerializeField] private Vector2 meleeIconFocus = new Vector2(0.5f, 0.5f);
    [Tooltip("Zoom and focus for single weapons whose pictures need different framing. Zoom below 1 leaves dark edges in the circle.")]
    [SerializeField] private List<IconFraming> iconFraming = new List<IconFraming>();

    [System.Serializable]
    public class IconFraming
    {
        public Sprite icon;
        public float zoom = 1f;
        public Vector2 focus = new Vector2(0.3f, 0.5f);
    }

    [Header("Health & Shield (top right)")]
    [SerializeField] private RectTransform healthFill;
    [SerializeField] private RectTransform healthChip;
    [SerializeField] private RectTransform shieldFill;
    [SerializeField] private RectTransform shieldChip;

    [Header("Animation")]
    [Tooltip("How fast the HUD fades in and out (alpha per second).")]
    [SerializeField] private float fadeSpeed = 6f;
    [Tooltip("How fast the ring fades in and out.")]
    [SerializeField] private float ringFadeSpeed = 8f;
    [Tooltip("How fast the health and shield bars move (full bars per second).")]
    [SerializeField] private float barSpeed = 2.5f;
    [Tooltip("Seconds the light 'damage' chip waits before shrinking to the new value.")]
    [SerializeField] private float chipDelay = 0.45f;
    [SerializeField] private float chipSpeed = 0.8f;
    [SerializeField] private float swapPopScale = 1.18f;
    [SerializeField] private float swapPopDuration = 0.3f;

    [Header("Melee")]
    [Tooltip("Ring color while a melee weapon (no heat) is out. The ring stays full.")]
    [SerializeField] private Color meleeRingColor = new Color32(0, 0, 0, 255);

    [Header("Overheat")]
    [Tooltip("Flashes per second on the ring and icon while overheated.")]
    [SerializeField] private float overheatPulseSpeed = 5f;
    [SerializeField] private Color overheatColor = new Color(1f, 0.15f, 0.1f);
    [Tooltip("Lowest ring opacity during the overheat flash.")]
    [Range(0f, 1f)] [SerializeField] private float overheatMinAlpha = 0.3f;

    private CanvasGroup group;
    private PlayerInteract player;
    private PlayerStats stats;
    private WeaponSwap weapons;
    private BlasterHeat heat;

    private Bar healthBar, shieldBar;
    private int shownWeapon = -2;
    private string weaponName = "";
    private float ringAlpha;
    private float popTime = float.MaxValue;
    private Color trackBaseColor = Color.white;
    private Sprite gradientRing;
    private Sprite solidRing;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        healthBar = new Bar(healthFill, healthChip);
        shieldBar = new Bar(shieldFill, shieldChip);

        if (heatTrack != null)
        {
            trackBaseColor = heatTrack.color;
            solidRing = heatTrack.sprite;
        }
        if (heatFill != null)
        {
            gradientRing = heatFill.sprite;
            heatFill.type = Image.Type.Filled;
            heatFill.fillMethod = Image.FillMethod.Radial360;
            heatFill.fillClockwise = true;
            heatFill.fillAmount = 0f;
        }
        SetRingAlpha(0f);
    }

    void Start()
    {
        player = FindAnyObjectByType<PlayerInteract>();
        weapons = FindAnyObjectByType<WeaponSwap>();
        if (weapons != null) heat = weapons.GetComponent<BlasterHeat>();
        stats = PlayerStats.Instance;

        if (weapons == null) Debug.LogWarning("DungeonHUD can't find a WeaponSwap on the Player.", this);
        if (stats == null) Debug.LogWarning("DungeonHUD can't find a PlayerStats. Add one to your Player.", this);

        if (stats != null)
        {
            healthBar.Snap(stats.HealthPercent);
            shieldBar.Snap(stats.ShieldPercent);
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        bool visible = (player == null || !player.IsInteracting) && (weapons == null || !weapons.IsLocked);
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, fadeSpeed * dt);

        if (stats == null) stats = PlayerStats.Instance;
        if (stats != null)
        {
            healthBar.Tick(stats.HealthPercent, dt, barSpeed, chipDelay, chipSpeed);
            shieldBar.Tick(stats.ShieldPercent, dt, barSpeed, chipDelay, chipSpeed);
        }

        UpdateWeapon();
        UpdateHeat(dt);
        TickPop(dt);
    }

    private void UpdateWeapon()
    {
        int index = weapons != null ? weapons.CurrentIndex : -1;
        if (index == shownWeapon) return;

        bool firstShow = shownWeapon == -2;
        shownWeapon = index;

        Transform model = null;
        if (weapons != null && index == 0 && weapons.melee != null) model = weapons.melee.weapon;
        if (weapons != null && index == 1 && weapons.gun != null) model = weapons.gun.weapon;

        string rawName = model != null ? model.name.Replace("(Clone)", "").Trim() : "";
        weaponName = PrettyName(rawName);
        Sprite icon = FindIcon(rawName);
        float zoom = iconZoom;
        Vector2 focus = index == 1 ? gunIconFocus : meleeIconFocus;
        foreach (IconFraming f in iconFraming)
        {
            if (f != null && f.icon != null && f.icon == icon)
            {
                zoom = f.zoom;
                focus = f.focus;
                break;
            }
        }
        SetIcon(icon, zoom, focus);

        if (weaponNameText != null) weaponNameText.text = weaponName;
        if (!firstShow) popTime = 0f;
    }

    private void UpdateHeat(float dt)
    {
        bool gunOut = weapons != null && weapons.GunOut && heat != null;
        bool meleeOut = weapons != null && weapons.MeleeOut;
        ringAlpha = Mathf.MoveTowards(ringAlpha, gunOut || meleeOut ? 1f : 0f, ringFadeSpeed * dt);
        SetRingAlpha(ringAlpha);

        bool overheated = gunOut && heat.IsOverheated;
        float pulse = overheated ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * overheatPulseSpeed * Mathf.PI * 2f) : 0f;

        if (heatFill != null)
        {
            if (meleeOut) heatFill.fillAmount = 1f;
            else if (heat != null) heatFill.fillAmount = heat.Heat;

            Sprite ringSprite = (overheated || meleeOut) && solidRing != null ? solidRing : gradientRing;
            if (heatFill.sprite != ringSprite) heatFill.sprite = ringSprite;

            Color c = meleeOut ? meleeRingColor : overheated ? overheatColor : Color.white;
            c.a = ringAlpha * (overheated ? Mathf.Lerp(overheatMinAlpha, 1f, pulse) : 1f);
            heatFill.color = c;
        }

        if (weaponIcon != null)
        {
            Color c = Color.Lerp(Color.white, overheatColor, pulse * 0.5f);
            c.a = weaponIcon.color.a;
            weaponIcon.color = c;
        }
    }

    private void SetRingAlpha(float a)
    {
        if (heatTrack != null)
        {
            Color c = trackBaseColor;
            c.a *= a;
            heatTrack.color = c;
        }
        if (heatFill != null)
        {
            Color c = heatFill.color;
            c.a = a;
            heatFill.color = c;
        }
    }

    private void TickPop(float dt)
    {
        if (weaponBadge == null || popTime >= swapPopDuration) return;

        popTime += dt;
        float k = Mathf.Clamp01(popTime / swapPopDuration);
        float easeOut = 1f - (1f - k) * (1f - k);
        weaponBadge.localScale = Vector3.one * Mathf.Lerp(swapPopScale, 1f, easeOut);
    }

    private Sprite FindIcon(string modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return null;
        foreach (Sprite s in weaponIcons)
            if (s != null && string.Equals(s.name, modelName, System.StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    // Sizes the icon so it covers the circle without stretching
    private void SetIcon(Sprite sprite, float zoom, Vector2 focus)
    {
        if (weaponIcon == null) return;

        weaponIcon.sprite = sprite;
        weaponIcon.enabled = sprite != null;
        if (sprite == null) return;

        RectTransform rect = weaponIcon.rectTransform;
        RectTransform parent = rect.parent as RectTransform;
        float d = parent != null ? Mathf.Min(parent.rect.width, parent.rect.height) : rect.rect.width;
        float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);

        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        Vector2 size = (aspect >= 1f ? new Vector2(d * aspect, d) : new Vector2(d, d / aspect)) * zoom;
        rect.sizeDelta = size;

        float x = (0.5f - focus.x) * size.x;
        float y = (0.5f - focus.y) * size.y;
        float roomX = Mathf.Max(0f, (size.x - d) * 0.5f);
        float roomY = Mathf.Max(0f, (size.y - d) * 0.5f);
        rect.anchoredPosition = new Vector2(Mathf.Clamp(x, -roomX, roomX), Mathf.Clamp(y, -roomY, roomY));
    }

    // Wpn_Blaster_SixShooter -> SIX SHOOTER
    private static string PrettyName(string modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return "";
        string last = modelName.Substring(modelName.LastIndexOf('_') + 1);
        return Regex.Replace(last, "(?<=[a-z])(?=[A-Z])", " ").ToUpperInvariant();
    }

    // A fill bar plus a lighter "chip" behind it that lingers after damage
    private class Bar
    {
        private readonly RectTransform fill, chip;
        private float shown, chipShown, lastTarget, hold;

        public Bar(RectTransform fill, RectTransform chip)
        {
            this.fill = fill;
            this.chip = chip;
        }

        public void Snap(float value)
        {
            shown = chipShown = lastTarget = Mathf.Clamp01(value);
            hold = 0f;
            Apply(fill, shown);
            Apply(chip, chipShown);
        }

        public void Tick(float target, float dt, float speed, float delay, float chipSpeed)
        {
            target = Mathf.Clamp01(target);
            if (target < lastTarget) hold = delay;
            lastTarget = target;

            shown = Mathf.MoveTowards(shown, target, speed * dt);

            if (chipShown <= shown) chipShown = shown;
            else if (hold > 0f) hold -= dt;
            else chipShown = Mathf.MoveTowards(chipShown, shown, chipSpeed * dt);

            Apply(fill, shown);
            Apply(chip, chipShown);
        }

        private static void Apply(RectTransform rect, float amount)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(amount, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
