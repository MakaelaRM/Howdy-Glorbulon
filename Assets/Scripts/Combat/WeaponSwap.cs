using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Put this on the Player Alien (next to NpcProceduralAnim). Two weapons: one melee, one gun.
//
//  Town scenes:     both weapons stay holstered and can't be drawn.
//  Dungeon scenes:  one weapon is always out. Press 1 for melee, 2 for the gun.
//
//  In hand:   the weapon is placed at his right hand, and the right arm stops swinging.
//  Holstered: melee on the hip and gun on the back (tick "Melee On Hip" off to swap them).
//
// All offsets are in the PLAYER's space: X = his right, Y = up, Z = forward. Units are metres.
// The numbers update live, so you can tweak them while the game is running.
[DefaultExecutionOrder(100)]
public class WeaponSwap : MonoBehaviour
{
    [Serializable]
    public class WeaponSlot
    {
        [Tooltip("The weapon (drag the model here, from the Project window or the Hierarchy).")]
        public Transform weapon;

        [Header("In hand")]
        [Tooltip("Move the weapon from the hand spot. X right, Y up, Z forward.")]
        public Vector3 heldOffset = Vector3.zero;
        [Tooltip("Rotation in degrees relative to the player.")]
        public Vector3 heldEuler = Vector3.zero;
        public Vector3 heldScale = Vector3.one;

        [Header("Holstered")]
        [Tooltip("Move the weapon from the hip/back spot. X right, Y up, Z forward.")]
        public Vector3 holsteredOffset = Vector3.zero;
        public Vector3 holsteredEuler = Vector3.zero;
        public Vector3 holsteredScale = Vector3.one;
    }

    [Header("Weapons")]
    public WeaponSlot melee = new WeaponSlot { holsteredEuler = new Vector3(180f, 0f, 0f) };
    public WeaponSlot gun = new WeaponSlot { holsteredEuler = new Vector3(-60f, 180f, 0f) };

    [Header("Holsters")]
    [Tooltip("On: melee on the hip and gun on the back. Off: swapped.")]
    public bool meleeOnHip = true;

    [Header("Arm while holding a weapon")]
    [Tooltip("Right arm angle while armed (it stops swinging). 0 = hangs naturally. Try 40 or -40 to hold it out in front.")]
    public float armHeldAngle = 0f;

    [Header("Swap animation")]
    [Tooltip("On: the arm lowers, the weapons swap, then the arm comes back up.")]
    public bool swapAnimation = true;
    [Tooltip("Total seconds for the arm to go down and back up.")]
    public float swapTime = 0.4f;
    [Tooltip("Arm angle at the bottom of the swap. 0 = hanging at his side.")]
    public float armDownAngle = 0f;

    [Header("Parts (found automatically by name: _Arm_R and _Body)")]
    public Transform rightArm;
    public Transform body;

    [Header("Where holstering is locked")]
    [Tooltip("Scene names where the weapons are locked away. Every other scene is a dungeon where one weapon is always out.")]
    public string[] townScenes = new string[] { "Town" };
    [Tooltip("Which weapon is drawn first when entering a dungeon. 0 = melee, 1 = gun.")]
    public int firstWeapon = 0;

    public event Action<int> OnWeaponChanged;

    public int CurrentIndex { get; private set; } = -1; // -1 = holstered, 0 = melee, 1 = gun
    public bool IsLocked { get; private set; }
    public bool IsArmed => CurrentIndex >= 0;
    public bool IsSwapping => swapping;   // other scripts can block attacks while this is true
    public bool MeleeOut => CurrentIndex == 0;
    public bool GunOut => CurrentIndex == 1;
    public int VisibleIndex => visualIndex;

    static int lastWeapon = -1; // remembered between scenes

    NpcProceduralAnim anim;
    int visualIndex = -1;   // which weapon is actually shown in the hand (changes halfway through a swap)
    bool swapping, swapSwitched;
    float swapTimer;
    Vector3 handInArm;   // hand spot, in the right arm's space
    Vector3 handInPlayer; // fallback if there is no arm
    Vector3 hipInBody;   // hip spot, in the body's space
    Vector3 backInBody;  // back spot, in the body's space
    Transform bodyT;

    void Awake()
    {
        anim = GetComponent<NpcProceduralAnim>();
        if (rightArm == null) rightArm = FindEndsWith("_Arm_R");
        if (body == null) body = FindEndsWith("_Body");
        bodyT = body != null ? body : transform;
        if (rightArm == null) Debug.LogWarning("[WeaponSwap] No right arm found (a child whose name ends with _Arm_R). Drag it into Right Arm.", this);

        ComputeSpots();

        ResolveWeapon(melee);
        ResolveWeapon(gun);
    }

    // Works out where the hand, hip and back are from the size of his body parts, before any animation moves them.
    void ComputeSpots()
    {
        // Hand: the bottom of the right arm.
        Bounds ab;
        if (rightArm != null && TryLocalBounds(rightArm, out ab))
        {
            handInPlayer = new Vector3(ab.center.x, ab.min.y + ab.size.y * 0.1f, ab.center.z);
            handInArm = rightArm.InverseTransformPoint(transform.TransformPoint(handInPlayer));
        }
        else
        {
            handInPlayer = new Vector3(0.3f, 0.4f, 0f);
            handInArm = rightArm != null ? rightArm.InverseTransformPoint(transform.TransformPoint(handInPlayer)) : Vector3.zero;
        }

        // Hip and back: from the body's size.
        Bounds bb;
        Vector3 hipP, backP;
        if (TryLocalBounds(bodyT == transform ? transform : bodyT, out bb))
        {
            Vector3 c = bb.center, e = bb.extents;
            hipP = c + new Vector3(e.x * 1.1f, -e.y * 0.25f, 0f);
            backP = c + new Vector3(0f, e.y * 0.2f, -e.z * 1.1f);
        }
        else
        {
            hipP = new Vector3(0.25f, 0.4f, 0f);
            backP = new Vector3(0f, 0.6f, -0.25f);
        }
        hipInBody = bodyT.InverseTransformPoint(transform.TransformPoint(hipP));
        backInBody = bodyT.InverseTransformPoint(transform.TransformPoint(backP));
    }

    // Bounds of all renderers under 'top', measured in the player's own space.
    bool TryLocalBounds(Transform top, out Bounds result)
    {
        result = new Bounds();
        bool any = false;
        foreach (var r in top.GetComponentsInChildren<Renderer>())
        {
            if (IsWeaponPart(r.transform)) continue;
            Bounds wb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = wb.center + Vector3.Scale(wb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = transform.InverseTransformPoint(corner);
                if (!any) { result = new Bounds(local, Vector3.zero); any = true; }
                else result.Encapsulate(local);
            }
        }
        return any;
    }

    void ResolveWeapon(WeaponSlot slot)
    {
        if (slot == null || slot.weapon == null) return;
        // Dragged in from the Project window? Make a copy in the scene.
        if (!slot.weapon.gameObject.scene.IsValid())
        {
            var copy = Instantiate(slot.weapon.gameObject);
            copy.name = slot.weapon.name;
            slot.weapon = copy.transform;
        }
        slot.weapon.SetParent(transform, true);
        slot.weapon.gameObject.SetActive(true);
    }

    void OnEnable() { SceneManager.activeSceneChanged += OnSceneChanged; }
    void OnDisable() { SceneManager.activeSceneChanged -= OnSceneChanged; }

    void Start() { ApplyScene(SceneManager.GetActiveScene()); }
    void OnSceneChanged(Scene from, Scene to) { ApplyScene(to); }

    void ApplyScene(Scene scene)
    {
        IsLocked = IsTown(scene.name);
        if (IsLocked) Equip(-1);
        else Equip(Mathf.Clamp(lastWeapon >= 0 ? lastWeapon : firstWeapon, 0, 1));
    }

    bool IsTown(string sceneName)
    {
        foreach (var n in townScenes)
            if (!string.IsNullOrEmpty(n) && string.Equals(n, sceneName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    void Update()
    {
        if (WasPressed(1)) TryEquip(0);
        else if (WasPressed(2)) TryEquip(1);
    }

    // Returns true if the swap happened. Does nothing in town (locked).
    public bool TryEquip(int index)
    {
        if (IsLocked) return false;
        if (index < 0 || index > 1) return false; // always need a weapon out in the dungeon
        if (index == CurrentIndex) return false;
        if (swapping) return false;
        Equip(index, true);
        return true;
    }

    // Lock or unlock from other scripts (for example a cutscene or a shop).
    public void SetLocked(bool locked)
    {
        if (locked == IsLocked) return;
        IsLocked = locked;
        if (locked) Equip(-1);
        else Equip(Mathf.Clamp(lastWeapon >= 0 ? lastWeapon : firstWeapon, 0, 1));
    }

    void Equip(int index, bool animate = false)
    {
        int previousVisual = visualIndex;
        CurrentIndex = index;
        if (index >= 0) lastWeapon = index;

        if (anim != null) anim.armRHeld = index >= 0;

        if (animate && swapAnimation && anim != null && index >= 0 && swapTime > 0f)
        {
            swapping = true;
            swapTimer = 0f;
            if (previousVisual >= 0)
            {
                swapSwitched = false;          // lower the arm first, switch weapons at the bottom
            }
            else
            {
                visualIndex = index;           // drawing from nothing: just raise the arm
                swapSwitched = true;
                swapTimer = swapTime * 0.5f;
            }
        }
        else
        {
            visualIndex = index;
            swapping = false;
        }

        if (OnWeaponChanged != null) OnWeaponChanged(index);
    }

    // Runs after the walk animation each frame, so the weapon follows the hand and the body.
    void LateUpdate()
    {
        float angle = armHeldAngle;
        if (swapping)
        {
            swapTimer += Time.deltaTime;
            float t = Mathf.Clamp01(swapTimer / swapTime);
            if (!swapSwitched && t >= 0.5f) { visualIndex = CurrentIndex; swapSwitched = true; }
            angle = t < 0.5f
                ? Mathf.SmoothStep(armHeldAngle, armDownAngle, t * 2f)
                : Mathf.SmoothStep(armDownAngle, armHeldAngle, (t - 0.5f) * 2f);
            if (t >= 1f) { swapping = false; angle = armHeldAngle; }
        }
        if (anim != null) anim.armRHeldAngle = angle;

        Place(melee, visualIndex == 0, meleeOnHip);
        Place(gun, visualIndex == 1, !meleeOnHip);
    }

    void Place(WeaponSlot slot, bool inHand, bool onHip)
    {
        if (slot == null || slot.weapon == null) return;

        Vector3 worldPos;
        Quaternion worldRot;
        Vector3 scale;

        if (inHand)
        {
            Vector3 hand = rightArm != null ? rightArm.TransformPoint(handInArm) : transform.TransformPoint(handInPlayer);
            worldPos = hand + transform.TransformVector(slot.heldOffset);
            worldRot = transform.rotation * Quaternion.Euler(slot.heldEuler);
            scale = slot.heldScale;
        }
        else
        {
            Vector3 spot = bodyT.TransformPoint(onHip ? hipInBody : backInBody);
            worldPos = spot + transform.TransformVector(slot.holsteredOffset);
            worldRot = transform.rotation * Quaternion.Euler(slot.holsteredEuler);
            scale = slot.holsteredScale;
        }

        slot.weapon.SetPositionAndRotation(worldPos, worldRot);
        Vector3 ls = transform.lossyScale;
        slot.weapon.localScale = new Vector3(
            ls.x != 0f ? scale.x / ls.x : scale.x,
            ls.y != 0f ? scale.y / ls.y : scale.y,
            ls.z != 0f ? scale.z / ls.z : scale.z);
    }

    // True for the weapons themselves and anything inside them.
    bool IsWeaponPart(Transform t)
    {
        for (var p = t; p != null && p != transform; p = p.parent)
        {
            if ((melee != null && p == melee.weapon) || (gun != null && p == gun.weapon)) return true;
            if (p.name.StartsWith("Wpn_", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    Transform FindEndsWith(string suffix)
    {
        suffix = suffix.ToLower();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform) continue;
            if (IsWeaponPart(t)) continue;
            if (t.name.ToLower().EndsWith(suffix)) return t;
        }
        return null;
    }

    // Works with either Unity input system.
    bool WasPressed(int number)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (number == 1 && (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame)) return true;
            if (number == 2 && (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame)) return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (number == 1 && (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))) return true;
        if (number == 2 && (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))) return true;
#endif
        return false;
    }
}