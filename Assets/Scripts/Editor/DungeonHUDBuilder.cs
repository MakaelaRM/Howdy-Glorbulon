#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// Editor tool: Tools > HUD > Create Dungeon HUD builds the dungeon HUD in the open scene
/// and wires it to a DungeonHUD component. Run it once in each dungeon scene (Mine, MineLevel1, ...).
/// Everything it makes can be edited by hand afterwards.
public static class DungeonHUDBuilder
{
    private const float Margin = 32f;

    private const string RingPath      = "Assets/UI/HUD/HUD_Ring.png";
    private const string HeatRingPath  = "Assets/UI/HUD/HUD_HeatRing.png";
    private const string HealthIcon    = "Assets/UI/Icons/Icon_Health.png";
    private const string ShieldIcon    = "Assets/UI/Icons/Icon_Shield.png";
    private const string WeaponPreviewFolder = "Assets/AI Assets/Town Assets/Previews/Weapons";

    private static readonly Color TrackColor  = new Color(0f, 0f, 0f, 1f);
    private static readonly Color RingTrack   = new Color(0f, 0f, 0f, 1f);
    private static readonly Color BadgeCenter = new Color(0.08f, 0.08f, 0.08f, 1f);
    private static readonly Color ChipColor   = new Color(1f, 1f, 1f, 0.75f);
    private static readonly Color PlateColor  = new Color32(98, 20, 210, 153);
    private static readonly Color HealthColor = new Color32(235, 40, 50, 255);
    private static readonly Color ShieldColor = new Color32(40, 205, 255, 255);

    [MenuItem("Tools/HUD/Create Dungeon HUD")]
    private static void Create()
    {
        Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        Sprite circle  = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        TMP_FontAsset font = FindFont("kleader");

        Sprite ring     = LoadSprite(RingPath);
        Sprite heatRing = LoadSprite(HeatRingPath);
        Sprite heart    = LoadSprite(HealthIcon);
        Sprite shield   = LoadSprite(ShieldIcon);

        var root = new GameObject("DungeonHUD", typeof(RectTransform), typeof(Canvas),
                                  typeof(CanvasScaler), typeof(CanvasGroup));
        Undo.RegisterCreatedObjectUndo(root, "Create Dungeon HUD");

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // Weapon: circle with the icon, heat ring around it, name plate behind
        const float ringSize = 168f;
        const float badgeSize = (ringSize * (214f / 256f) + 4f) * 1.1f;

        RectTransform weapon = CreateRect("Weapon", root.transform);
        PinToCorner(weapon, new Vector2(0f, 1f), new Vector2(Margin, -Margin));
        weapon.sizeDelta = new Vector2(ringSize, ringSize);

        RectTransform plate = CreateRect("NamePlate", weapon);
        plate.anchorMin = plate.anchorMax = new Vector2(0.5f, 0.5f);
        plate.pivot = new Vector2(0f, 0.5f);
        plate.anchoredPosition = Vector2.zero;
        plate.sizeDelta = new Vector2(ringSize * 0.5f + 200f, 54f);
        Style(plate.gameObject.AddComponent<Image>(), rounded, PlateColor);

        const float plateCushion = 16f;
        var plateRow = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
        plateRow.padding = new RectOffset(Mathf.RoundToInt(ringSize * 0.5f + plateCushion), 32, 0, 0);
        plateRow.childAlignment = TextAnchor.MiddleLeft;
        plateRow.childControlWidth = true;
        plateRow.childControlHeight = true;
        plateRow.childForceExpandWidth = false;
        plateRow.childForceExpandHeight = true;

        var plateFit = plate.gameObject.AddComponent<ContentSizeFitter>();
        plateFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        plateFit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        TextMeshProUGUI nameText = CreateText("WeaponName", plate, font, 30f, TextAlignmentOptions.MidlineLeft);
        nameText.text = "WEAPON";

        RectTransform circleGroup = CreateRect("Circle", weapon);
        Stretch(circleGroup, 0f, 0f, 0f, 0f);

        RectTransform badge = CreateRect("Badge", circleGroup);
        badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 0.5f);
        badge.pivot = new Vector2(0.5f, 0.5f);
        badge.anchoredPosition = Vector2.zero;
        badge.sizeDelta = new Vector2(badgeSize, badgeSize);
        Style(badge.gameObject.AddComponent<Image>(), circle, BadgeCenter);
        badge.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        RectTransform iconRect = CreateRect("Icon", badge);
        Stretch(iconRect, 0f, 0f, 0f, 0f);
        Image weaponIcon = iconRect.gameObject.AddComponent<Image>();
        Style(weaponIcon, null, Color.white);
        weaponIcon.preserveAspect = true;

        RectTransform track = CreateRect("HeatTrack", circleGroup);
        Stretch(track, 0f, 0f, 0f, 0f);
        Image trackImage = track.gameObject.AddComponent<Image>();
        Style(trackImage, ring, RingTrack);

        RectTransform fill = CreateRect("HeatFill", circleGroup);
        Stretch(fill, 0f, 0f, 0f, 0f);
        Image heatImage = fill.gameObject.AddComponent<Image>();
        Style(heatImage, heatRing, Color.white);
        heatImage.type = Image.Type.Filled;
        heatImage.fillMethod = Image.FillMethod.Radial360;
        heatImage.fillOrigin = (int)Image.Origin360.Top;
        heatImage.fillClockwise = true;
        heatImage.fillAmount = 0.65f;

        // Health and shield bars, styled like the town reputation bar but smaller
        const float barWidth = 400f;
        const float barHeight = 30f;
        const float barBadge = 50f;
        const float barGap = 14f;

        RectTransform healthChip, shieldChip;
        RectTransform healthFill = CreateBar(root.transform, "Health", 0, barWidth, barHeight, barBadge, barGap,
                                             rounded, circle, heart, HealthColor, out healthChip);
        RectTransform shieldFill = CreateBar(root.transform, "Shield", 1, barWidth, barHeight, barBadge, barGap,
                                             rounded, circle, shield, ShieldColor, out shieldChip);

        var hud = root.AddComponent<DungeonHUD>();
        var so = new SerializedObject(hud);
        so.FindProperty("weaponIcon").objectReferenceValue = weaponIcon;
        so.FindProperty("weaponBadge").objectReferenceValue = circleGroup;
        so.FindProperty("weaponNameText").objectReferenceValue = nameText;
        so.FindProperty("heatFill").objectReferenceValue = heatImage;
        so.FindProperty("heatTrack").objectReferenceValue = trackImage;
        so.FindProperty("healthFill").objectReferenceValue = healthFill;
        so.FindProperty("healthChip").objectReferenceValue = healthChip;
        so.FindProperty("shieldFill").objectReferenceValue = shieldFill;
        so.FindProperty("shieldChip").objectReferenceValue = shieldChip;

        List<Sprite> icons = LoadWeaponIcons();
        SerializedProperty list = so.FindProperty("weaponIcons");
        list.arraySize = icons.Count;
        for (int i = 0; i < icons.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = icons[i];

        Sprite atomizer = icons.Find(i => i.name == "Wpn_Blaster_Atomizer");
        if (atomizer != null)
        {
            SerializedProperty framing = so.FindProperty("iconFraming");
            framing.arraySize = 1;
            SerializedProperty entry = framing.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("icon").objectReferenceValue = atomizer;
            entry.FindPropertyRelative("zoom").floatValue = 1f;
            entry.FindPropertyRelative("focus").vector2Value = new Vector2(0.3f, 0.5f);
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        PreviewGunIcon(weaponIcon, icons);
        EnsurePlayerComponents();

        Selection.activeGameObject = root;
        Debug.Log("Dungeon HUD created with " + icons.Count + " weapon icons. Run this once in each dungeon scene.", root);
    }

    private static RectTransform CreateBar(Transform parent, string label, int row, float width, float height,
                                           float badgeSize, float gap, Sprite rounded, Sprite circle, Sprite icon,
                                           Color color, out RectTransform chip)
    {
        float y = -Margin - (badgeSize - height) * 0.5f - row * (badgeSize + gap);

        RectTransform bar = CreateRect(label, parent);
        PinToCorner(bar, new Vector2(1f, 1f), new Vector2(-Margin, y));
        bar.sizeDelta = new Vector2(width, height);

        RectTransform track = CreateRect("Track", bar);
        Stretch(track, 0f, 0f, 0f, 0f);
        Style(track.gameObject.AddComponent<Image>(), rounded, TrackColor);

        RectTransform fillArea = CreateRect("FillArea", track);
        Stretch(fillArea, badgeSize * 0.5f + 4f, 5f, 5f, 5f);

        chip = CreateRect("Chip", fillArea);
        Stretch(chip, 0f, 0f, 0f, 0f);
        Style(chip.gameObject.AddComponent<Image>(), rounded, ChipColor);

        RectTransform fill = CreateRect("Fill", fillArea);
        Stretch(fill, 0f, 0f, 0f, 0f);
        Style(fill.gameObject.AddComponent<Image>(), rounded, color);

        RectTransform badge = CreateRect("Badge", bar);
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
        badge.pivot = new Vector2(0.5f, 0.5f);
        badge.anchoredPosition = Vector2.zero;
        badge.sizeDelta = new Vector2(badgeSize, badgeSize);
        Style(badge.gameObject.AddComponent<Image>(), circle, color);

        RectTransform center = CreateRect("Center", badge);
        Stretch(center, 4f, 4f, 4f, 4f);
        Style(center.gameObject.AddComponent<Image>(), circle, BadgeCenter);

        RectTransform iconRect = CreateRect("Icon", center);
        Stretch(iconRect, 8f, 8f, 8f, 8f);
        Image iconImage = iconRect.gameObject.AddComponent<Image>();
        Style(iconImage, icon != null ? icon : circle, color);
        iconImage.preserveAspect = true;

        return fill;
    }

    // Shows the player's gun in the circle so the layout looks right in edit mode
    private static void PreviewGunIcon(Image icon, List<Sprite> icons)
    {
        var weapons = Object.FindAnyObjectByType<WeaponSwap>();
        string gunName = weapons != null && weapons.gun != null && weapons.gun.weapon != null ? weapons.gun.weapon.name : null;
        foreach (Sprite s in icons)
        {
            if (gunName != null && s.name == gunName)
            {
                icon.sprite = s;
                break;
            }
        }
        icon.enabled = icon.sprite != null;
    }

    private static List<Sprite> LoadWeaponIcons()
    {
        var icons = new List<Sprite>();
        if (!AssetDatabase.IsValidFolder(WeaponPreviewFolder))
        {
            Debug.LogWarning("Couldn't find " + WeaponPreviewFolder + ". Drag weapon icon sprites into DungeonHUD > Weapon Icons.");
            return icons;
        }
        foreach (string guid in AssetDatabase.FindAssets("Wpn_ t:Texture2D", new[] { WeaponPreviewFolder }))
        {
            Sprite s = LoadSprite(AssetDatabase.GUIDToAssetPath(guid));
            if (s != null) icons.Add(s);
        }
        return icons;
    }

    // Switches a texture to Sprite import if needed, then loads it
    private static Sprite LoadSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning("Couldn't find a texture at " + path + ", using a placeholder.");
            return null;
        }
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void EnsurePlayerComponents()
    {
        var weapons = Object.FindAnyObjectByType<WeaponSwap>();
        if (weapons == null)
        {
            Debug.LogWarning("No WeaponSwap in this scene. The weapon icon and heat ring need one on the Player.");
        }
        else if (weapons.GetComponent<BlasterAttack>() != null && weapons.GetComponent<BlasterHeat>() == null)
        {
            Undo.AddComponent<BlasterHeat>(weapons.gameObject);
            Debug.Log("Added BlasterHeat to " + weapons.name + ".", weapons);
        }

        if (Object.FindAnyObjectByType<PlayerStats>() != null) return;

        GameObject target = weapons != null ? weapons.gameObject : null;
        if (target == null)
        {
            var player = Object.FindAnyObjectByType<PlayerInteract>();
            if (player != null) target = player.gameObject;
        }

        if (target != null)
        {
            Undo.AddComponent<PlayerStats>(target);
            Debug.Log("Added PlayerStats to " + target.name + ".", target);
        }
        else
        {
            Debug.LogWarning("No PlayerStats in the scene. Add one to your Player so the bars have numbers to show.");
        }
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset font,
                                              float size, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void PinToCorner(RectTransform rect, Vector2 corner, Vector2 offset)
    {
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
    }

    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void Style(Image image, Sprite sprite, Color color)
    {
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
    }

    private static TMP_FontAsset FindFont(string search)
    {
        foreach (string guid in AssetDatabase.FindAssets(search + " t:TMP_FontAsset"))
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (font != null) return font;
        }
        Debug.LogWarning("Couldn't find the Kleader font asset, using TextMeshPro's default font.");
        return TMP_Settings.defaultFontAsset;
    }
}
#endif
