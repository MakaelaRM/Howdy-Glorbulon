using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(150)]
[RequireComponent(typeof(WeaponSwap))]
public class MeleeAttack : MonoBehaviour
{
    [Header("Hit")]
    [Tooltip("How far the swipe reaches from the player, in metres.")]
    public float range = 1.5f;
    [Tooltip("Width of the swipe in degrees. 180 = everything in front of him.")]
    public float arcAngle = 150f;
    [Tooltip("Height of the swipe above the player's feet, in metres.")]
    public float hitHeight = 0.5f;
    public LayerMask hitLayers = ~0;
    [Tooltip("Seconds after a swipe finishes before the next one can start.")]
    public float cooldown = 0.1f;

    [Header("Swipe animation")]
    [Tooltip("Seconds for the whole swipe: wind-up, strike and recovery.")]
    public float swingTime = 0.45f;
    [Range(0.05f, 0.6f)] public float windUpPart = 0.25f;
    [Range(0.05f, 0.6f)] public float strikePart = 0.3f;
    [Tooltip("How far the arm pulls back to his right before striking, in degrees.")]
    public float windUpAngle = 60f;
    [Tooltip("How far the arm follows through to his left, in degrees.")]
    public float followThroughAngle = 75f;
    [Tooltip("Arm tilt at the wind-up, in degrees. Negative raises the arm.")]
    public float windUpLift = -25f;
    [Tooltip("Arm tilt at the follow-through, in degrees. Positive lowers the arm.")]
    public float followThroughDrop = 15f;
    [Tooltip("How far the blade tips forward during the swipe, in degrees.")]
    public float bladeTilt = 70f;
    [Tooltip("Extra wrist flick, as a fraction of the arm's sweep.")]
    public float wristFlick = 0.35f;

    [Header("Trail")]
    public bool showTrail = true;
    [Tooltip("Leave empty to use a plain unlit material.")]
    public Material trailMaterial;
    public Color trailColor = new Color(0.45f, 1f, 1f, 0.85f);
    public float trailWidth = 0.18f;
    public float trailTime = 0.12f;

    [Header("Sound (optional)")]
    public AudioClip swingSound;
    public AudioClip hitSound;

    public bool IsAttacking => swinging;

    WeaponSwap weapons;
    CharacterMovement movement;
    AudioSource audioSource;
    bool swinging, hitDone;
    float timer, cooldownTimer;
    Transform trailPoint;
    TrailRenderer trail;
    Transform tipWeapon;
    Vector3 tipLocal;
    readonly HashSet<EnemyHitReaction> hitThisSwing = new HashSet<EnemyHitReaction>();

    void Awake()
    {
        weapons = GetComponent<WeaponSwap>();
        audioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        movement = FindAnyObjectByType<CharacterMovement>();
        if (showTrail) CreateTrail();
    }

    void Update()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        if (swinging && (!weapons.MeleeOut || weapons.IsSwapping)) StopSwing();

        if (!swinging && cooldownTimer <= 0f && ClickedThisFrame() && CanStartSwing()) StartSwing();
    }

    bool ClickedThisFrame()
    {
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    bool CanStartSwing()
    {
        if (!weapons.MeleeOut || weapons.IsSwapping) return false;
        if (Time.timeScale <= 0f) return false;
        if (movement != null && movement.inventoryUI != null && movement.inventoryUI.activeInHierarchy) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
        return true;
    }

    void StartSwing()
    {
        swinging = true;
        hitDone = false;
        timer = 0f;
        hitThisSwing.Clear();

        Transform blade = weapons.melee != null ? weapons.melee.weapon : null;
        if (blade != null && blade != tipWeapon)
        {
            tipWeapon = blade;
            tipLocal = FindTip(blade);
        }

        if (trail != null)
        {
            trail.emitting = false;
            trail.Clear();
        }

        if (swingSound != null && audioSource != null) audioSource.PlayOneShot(swingSound);
    }

    void StopSwing()
    {
        swinging = false;
        cooldownTimer = cooldown;
        if (trail != null) trail.emitting = false;
    }

    void LateUpdate()
    {
        if (!swinging) return;

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / Mathf.Max(0.05f, swingTime));
        float w = windUpPart;
        float s = Mathf.Min(strikePart, 1f - w);
        float yaw, pitch, envelope;
        bool striking = false;

        if (t < w)
        {
            float a = Mathf.SmoothStep(0f, 1f, t / w);
            yaw = windUpAngle * a;
            pitch = windUpLift * a;
            envelope = a;
        }
        else if (t < w + s)
        {
            float a = (t - w) / s;
            float e = 1f - (1f - a) * (1f - a) * (1f - a);
            yaw = Mathf.Lerp(windUpAngle, -followThroughAngle, e);
            pitch = Mathf.Lerp(windUpLift, followThroughDrop, e);
            envelope = 1f;
            striking = true;
        }
        else
        {
            float a = Mathf.SmoothStep(0f, 1f, (t - w - s) / Mathf.Max(0.0001f, 1f - w - s));
            yaw = Mathf.Lerp(-followThroughAngle, 0f, a);
            pitch = Mathf.Lerp(followThroughDrop, 0f, a);
            envelope = 1f - a;
        }

        ApplyPose(yaw, pitch, envelope);

        if (trail != null) trail.emitting = striking;

        if (!hitDone && t >= w && (yaw <= 0f || t >= w + s * 0.5f)) DoHit();

        if (t >= 1f) StopSwing();
    }

    void ApplyPose(float yaw, float pitch, float envelope)
    {
        Transform arm = weapons.rightArm;
        Transform blade = weapons.melee != null ? weapons.melee.weapon : null;
        Vector3 up = transform.up;
        Vector3 right = transform.right;
        Quaternion sweep = Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, right);
        Vector3 pivot = arm != null ? arm.position : transform.TransformPoint(new Vector3(0.3f, 0.8f, 0f));

        if (arm != null) arm.rotation = sweep * arm.rotation;

        if (blade != null)
        {
            blade.position = pivot + sweep * (blade.position - pivot);
            Quaternion wrist = Quaternion.AngleAxis(yaw * wristFlick, up) * Quaternion.AngleAxis(bladeTilt * envelope, sweep * right);
            blade.rotation = wrist * sweep * blade.rotation;

            if (trailPoint != null) trailPoint.position = blade.TransformPoint(tipLocal);
        }
    }

    void DoHit()
    {
        hitDone = true;
        Vector3 origin = transform.position + Vector3.up * hitHeight;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        bool hitSomething = false;

        foreach (Collider c in Physics.OverlapSphere(origin, range, hitLayers, QueryTriggerInteraction.Collide))
        {
            if (c == null || c.transform.IsChildOf(transform)) continue;

            EnemyHitReaction enemy = EnemyHitReaction.FindOn(c);
            if (enemy == null || hitThisSwing.Contains(enemy)) continue;

            Vector3 to = enemy.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f && Vector3.Angle(forward, to) > arcAngle * 0.5f) continue;

            hitThisSwing.Add(enemy);
            enemy.TakeHit(transform.position);
            hitSomething = true;
        }

        if (hitSomething && hitSound != null && audioSource != null) audioSource.PlayOneShot(hitSound);
    }

    Vector3 FindTip(Transform weapon)
    {
        Vector3 best = Vector3.up * 0.3f;
        float bestDist = -1f;
        foreach (MeshFilter mf in weapon.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Bounds b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = weapon.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (local.sqrMagnitude > bestDist)
                {
                    bestDist = local.sqrMagnitude;
                    best = local;
                }
            }
        }
        return best;
    }

    void CreateTrail()
    {
        Material mat = trailMaterial;
        if (mat == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) mat = new Material(shader);
        }
        if (mat == null) return;

        var go = new GameObject("Wpn_SwipeTrail");
        go.transform.SetParent(transform, false);
        trailPoint = go.transform;

        trail = go.AddComponent<TrailRenderer>();
        trail.material = mat;
        trail.time = trailTime;
        trail.minVertexDistance = 0.02f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        trail.widthMultiplier = trailWidth;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;

        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(trailColor, 0f), new GradientColorKey(trailColor, 1f) },
            new[] { new GradientAlphaKey(trailColor.a, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Vector3 origin = transform.position + Vector3.up * hitHeight;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();

        const int steps = 16;
        Vector3 prev = origin;
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(-arcAngle * 0.5f, arcAngle * 0.5f, i / (float)steps);
            Vector3 p = origin + Quaternion.AngleAxis(a, Vector3.up) * forward * range;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
        Gizmos.DrawLine(prev, origin);
    }
}
