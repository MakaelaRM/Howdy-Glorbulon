using System.Collections.Generic;
using UnityEngine;

/// Put this on anything the player can shoot (each Nibbler, egg clusters, bosses).
/// - Registers it for PlayerAim's depth-assisted aiming.
/// - Draws a ring on the ground under it, so you can see exactly where it is on the floor
///   even from a low camera. The ring shrinks while the enemy is in the air (bouncing),
///   and turns gold and grows when it is the current aim target.
public class AimTarget : MonoBehaviour
{
    public static readonly List<AimTarget> All = new List<AimTarget>();

    [Tooltip("Height above this object's origin that shots aim at (roughly the middle of the body).")]
    public float aimHeight = 0.25f;
    public bool canBeTargeted = true;

    [Header("Ground Ring")]
    public bool showRing = true;
    public float ringRadius = 0.32f;
    public float ringWidth = 0.045f;
    public Color ringColor = new Color(1f, 0.35f, 0.45f, 0.85f);
    public Color targetedColor = new Color(1f, 0.85f, 0.25f, 1f);
    public float targetedScale = 1.35f;
    [Tooltip("What counts as floor for placing the ring.")]
    public LayerMask groundLayers = ~0;
    [Tooltip("Optional. Leave empty to use a simple built-in material. If the ring shows pink, assign an Unlit material here.")]
    public Material ringMaterial;

    /// Uses EnemyHitReaction.Center when the enemy has one, otherwise origin + Aim Height.
    public Vector3 AimPoint => reaction != null ? reaction.Center : transform.position + Vector3.up * aimHeight;
    public bool IsTargeted { get; private set; }
    /// Alive (per your EnemyStats) and targetable.
    public bool CanBeTargeted => canBeTargeted && !IsDead;
    bool IsDead => reaction != null && EnemyStats.IsDeadOn(reaction);

    EnemyHitReaction reaction;

    const int Segments = 28;
    LineRenderer ring;
    float pulse;

    void OnEnable() { All.Add(this); if (ring) ring.gameObject.SetActive(showRing); }
    void OnDisable() { All.Remove(this); if (ring) ring.gameObject.SetActive(false); }
    void OnDestroy() { if (ring) Destroy(ring.gameObject); }

    public void SetTargeted(bool on)
    {
        if (on && !IsTargeted) pulse = 1f;
        IsTargeted = on;
    }

    void Awake()
    {
        reaction = GetComponentInChildren<EnemyHitReaction>();
        if (reaction == null) reaction = GetComponentInParent<EnemyHitReaction>();
    }

    void Start()
    {
        if (!showRing) return;
        // The ring is NOT a child, so squash-and-stretch on the enemy doesn't squash the ring.
        var go = new GameObject(name + "_AimRing");
        ring = go.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = Segments;
        ring.widthMultiplier = ringWidth;
        ring.numCornerVertices = 2;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.alignment = LineAlignment.TransformZ;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // lie flat on the ground
        if (ringMaterial) ring.sharedMaterial = ringMaterial;
        else
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh) ring.sharedMaterial = new Material(sh);
        }
    }

    void LateUpdate()
    {
        if (!ring) return;
        if (IsDead) { if (ring.enabled) { ring.enabled = false; SetTargeted(false); } return; }

        // find the floor under the enemy, ignoring the enemy's own colliders
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        float groundY = transform.position.y, height = 0f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 30f, groundLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best) { best = h.distance; groundY = h.point.y; }
        }
        if (best < float.MaxValue) height = Mathf.Max(0f, transform.position.y - groundY);

        pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 4f);
        float scale = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(height / 1.5f));
        if (IsTargeted) scale *= targetedScale + pulse * 0.4f;
        float r = ringRadius * scale;

        Color c = IsTargeted ? targetedColor : ringColor;
        ring.startColor = c; ring.endColor = c;
        ring.widthMultiplier = ringWidth * (IsTargeted ? 1.4f : 1f);

        Vector3 centre = new Vector3(transform.position.x, groundY + 0.03f, transform.position.z);
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            ring.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
    }
}