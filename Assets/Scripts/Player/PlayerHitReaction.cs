using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class PlayerHitReaction : MonoBehaviour
{
    [Header("Contact")]
    [Tooltip("Extra reach around the player's collider that counts as an enemy touching him, in metres. The Nibblers stop just short of him, so this needs to be above 0.")]
    public float contactPadding = 0.2f;
    [Tooltip("Seconds before the same enemy can hit him again. Other enemies have their own timers.")]
    public float hitCooldown = 1f;
    [Tooltip("On: stunned enemies can't hurt him.")]
    public bool ignoreStunnedEnemies = true;
    public LayerMask enemyLayers = ~0;

    [Header("Glow")]
    public Color glowColor = new Color(1f, 0.05f, 0.05f);
    [Tooltip("Seconds the red glow takes to fade out.")]
    public float glowTime = 0.5f;

    [Header("Knockback")]
    [Tooltip("How far a hit pushes him away from the enemy, in metres.")]
    public float knockback = 0.5f;
    public float knockbackTime = 0.15f;

    [Header("Sound and events (optional)")]
    public AudioClip hurtSound;
    public UnityEvent onHit;

    public event Action<EnemyHitReaction> Hit;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    readonly Dictionary<EnemyHitReaction, float> nextHitTime = new Dictionary<EnemyHitReaction, float>();
    readonly List<EnemyHitReaction> expired = new List<EnemyHitReaction>();
    CharacterController controller;
    AudioSource audioSource;
    Renderer[] renderers;
    int[][] colorIds;
    Color[][] baseColors;
    MaterialPropertyBlock block;
    float glowTimer = -1f;
    float knockTimer;
    Vector3 knockDir;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        audioSource = GetComponent<AudioSource>();
        block = new MaterialPropertyBlock();
    }

    void Start()
    {
        CacheRenderers();
    }

    void CacheRenderers()
    {
        var list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if ((r is MeshRenderer || r is SkinnedMeshRenderer) && !IsWeaponPart(r.transform)) list.Add(r);
        renderers = list.ToArray();

        colorIds = new int[renderers.Length][];
        baseColors = new Color[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] mats = renderers[i].sharedMaterials;
            colorIds[i] = new int[mats.Length];
            baseColors[i] = new Color[mats.Length];
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                int id = -1;
                if (mat != null)
                {
                    if (mat.HasProperty(BaseColorId)) id = BaseColorId;
                    else if (mat.HasProperty(ColorId)) id = ColorId;
                }
                colorIds[i][m] = id;
                baseColors[i][m] = id != -1 ? mat.GetColor(id) : Color.white;
            }
        }
    }

    bool IsWeaponPart(Transform t)
    {
        for (Transform p = t; p != null && p != transform; p = p.parent)
            if (p.name.StartsWith("Wpn_", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        CheckContacts();

        if (knockTimer > 0f && knockbackTime > 0f)
        {
            float k = knockTimer / knockbackTime;
            Vector3 step = knockDir * (2f * knockback / knockbackTime) * k * Mathf.Min(dt, knockTimer);
            if (controller != null && controller.enabled) controller.Move(step);
            else transform.position += step;
            knockTimer -= dt;
        }

        if (glowTimer >= 0f)
        {
            glowTimer += dt;
            if (glowTimer >= glowTime)
            {
                glowTimer = -1f;
                ClearGlow();
            }
            else
            {
                ApplyGlow(Mathf.SmoothStep(1f, 0f, glowTimer / glowTime));
            }
        }
    }

    void CheckContacts()
    {
        if (Time.timeScale <= 0f) return;

        Vector3 p1, p2;
        float radius;
        if (controller != null)
        {
            Vector3 scale = transform.lossyScale;
            float r = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float half = Mathf.Max(0f, controller.height * 0.5f * Mathf.Abs(scale.y) - r);
            Vector3 center = transform.TransformPoint(controller.center);
            p1 = center + Vector3.up * half;
            p2 = center - Vector3.up * half;
            radius = r + contactPadding;
        }
        else
        {
            p1 = p2 = transform.position + Vector3.up * 0.5f;
            radius = 0.5f + contactPadding;
        }

        foreach (Collider c in Physics.OverlapCapsule(p1, p2, radius, enemyLayers, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(transform)) continue;

            EnemyHitReaction enemy = EnemyHitReaction.FindOn(c);
            if (enemy == null) continue;
            if (ignoreStunnedEnemies && enemy.IsStunned) continue;
            if (nextHitTime.TryGetValue(enemy, out float allowedAt) && Time.time < allowedAt) continue;

            nextHitTime[enemy] = Time.time + hitCooldown;
            TakeHit(enemy);
        }

        CleanUpTimers();
    }

    void CleanUpTimers()
    {
        if (nextHitTime.Count == 0) return;
        expired.Clear();
        foreach (var pair in nextHitTime)
            if (pair.Key == null || Time.time >= pair.Value) expired.Add(pair.Key);
        foreach (EnemyHitReaction e in expired) nextHitTime.Remove(e);
    }

    public void TakeHit(EnemyHitReaction enemy)
    {
        Vector3 from = enemy != null ? enemy.transform.position : transform.position - transform.forward;
        knockDir = transform.position - from;
        knockDir.y = 0f;
        knockDir = knockDir.sqrMagnitude > 1e-4f ? knockDir.normalized : -transform.forward;
        knockTimer = knockbackTime;

        glowTimer = 0f;
        ApplyGlow(1f);

        if (hurtSound != null && audioSource != null) audioSource.PlayOneShot(hurtSound);
        onHit.Invoke();
        Hit?.Invoke(enemy);
    }

    void ApplyGlow(float amount)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            for (int m = 0; m < colorIds[i].Length; m++)
            {
                int id = colorIds[i][m];
                if (id == -1) continue;
                Color c = Color.Lerp(baseColors[i][m], glowColor, amount);
                c.a = baseColors[i][m].a;
                block.Clear();
                block.SetColor(id, c);
                r.SetPropertyBlock(block, m);
            }
        }
    }

    void ClearGlow()
    {
        if (renderers == null) return;
        block.Clear();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            for (int m = 0; m < colorIds[i].Length; m++)
                if (colorIds[i][m] != -1) r.SetPropertyBlock(block, m);
        }
    }

    void OnDisable()
    {
        if (glowTimer >= 0f)
        {
            glowTimer = -1f;
            ClearGlow();
        }
    }

    void OnDrawGizmosSelected()
    {
        CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
        if (cc == null) return;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        Vector3 center = transform.TransformPoint(cc.center);
        float r = cc.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) + contactPadding;
        float half = Mathf.Max(0f, cc.height * 0.5f * Mathf.Abs(transform.lossyScale.y) - (r - contactPadding));
        Gizmos.DrawWireSphere(center + Vector3.up * half, r);
        Gizmos.DrawWireSphere(center - Vector3.up * half, r);
    }
}
