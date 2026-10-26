using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class EnemyHitReaction : MonoBehaviour
{
    [Header("Flash")]
    public Color flashColor = new Color(1f, 0.05f, 0.05f);
    [Tooltip("Seconds the enemy flashes red after being hit.")]
    public float flashTime = 1f;
    [Tooltip("How many times it blinks during the flash. 0 = one solid flash that fades out.")]
    public int flashBlinks = 3;

    [Header("Stun")]
    [Tooltip("Seconds the enemy is stunned after being hit.")]
    public float stunTime = 0.6f;
    [Tooltip("How far a hit pushes it away from the attacker, in metres.")]
    public float knockback = 0.35f;
    public float knockbackTime = 0.15f;
    [Tooltip("Only for enemies without PestChomper: scripts to switch off while stunned.")]
    public Behaviour[] pauseWhileStunned;

    public UnityEvent onHit;

    public bool IsStunned => stunTimer > 0f;

    public Vector3 Center
    {
        get
        {
            bool any = false;
            Bounds b = new Bounds();
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (!any)
                {
                    b = r.bounds;
                    any = true;
                }
                else b.Encapsulate(r.bounds);
            }
            return any ? b.center : transform.position;
        }
    }

    public static EnemyHitReaction FindOn(Collider c)
    {
        if (c == null) return null;
        EnemyHitReaction reaction = c.GetComponentInParent<EnemyHitReaction>();
        if (reaction != null) return reaction;

        AlienWildWest.PestChomper pest = c.GetComponentInParent<AlienWildWest.PestChomper>();
        return pest != null ? pest.gameObject.AddComponent<EnemyHitReaction>() : null;
    }

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    AlienWildWest.PestChomper pest;
    Renderer[] renderers;
    int[][] colorIds;
    Color[][] baseColors;
    MaterialPropertyBlock block;
    float flashTimer = -1f;
    float stunTimer, knockTimer;
    Vector3 knockDir;

    void Awake()
    {
        pest = GetComponent<AlienWildWest.PestChomper>();
        block = new MaterialPropertyBlock();
        CacheRenderers();
    }

    void CacheRenderers()
    {
        var list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
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

    public void TakeHit(Vector3 attackerPosition)
    {
        flashTimer = 0f;
        stunTimer = Mathf.Max(stunTimer, stunTime);

        knockDir = transform.position - attackerPosition;
        knockDir.y = 0f;
        knockDir = knockDir.sqrMagnitude > 1e-4f ? knockDir.normalized : -transform.forward;
        knockTimer = knockbackTime;

        if (pest != null) pest.Stun(stunTime);
        else SetPaused(true);

        ApplyFlash(1f);
        onHit.Invoke();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (knockTimer > 0f && knockbackTime > 0f)
        {
            float k = knockTimer / knockbackTime;
            transform.position += knockDir * (2f * knockback / knockbackTime) * k * Mathf.Min(dt, knockTimer);
            knockTimer -= dt;
        }

        if (stunTimer > 0f)
        {
            stunTimer -= dt;
            if (stunTimer <= 0f && pest == null) SetPaused(false);
        }

        if (flashTimer >= 0f)
        {
            flashTimer += dt;
            if (flashTimer >= flashTime)
            {
                flashTimer = -1f;
                ClearFlash();
            }
            else
            {
                ApplyFlash(FlashAmount(flashTimer / flashTime));
            }
        }
    }

    float FlashAmount(float t)
    {
        float fade = Mathf.Sqrt(1f - t);
        if (flashBlinks <= 0) return fade;
        return fade * (0.5f + 0.5f * Mathf.Cos(t * flashBlinks * 2f * Mathf.PI));
    }

    void ApplyFlash(float amount)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            for (int m = 0; m < colorIds[i].Length; m++)
            {
                int id = colorIds[i][m];
                if (id == -1) continue;
                Color c = Color.Lerp(baseColors[i][m], flashColor, amount);
                c.a = baseColors[i][m].a;
                block.Clear();
                block.SetColor(id, c);
                r.SetPropertyBlock(block, m);
            }
        }
    }

    void ClearFlash()
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

    void SetPaused(bool paused)
    {
        if (pauseWhileStunned == null) return;
        foreach (Behaviour b in pauseWhileStunned)
            if (b != null && b != this) b.enabled = !paused;
    }

    void OnDisable()
    {
        if (flashTimer >= 0f)
        {
            flashTimer = -1f;
            ClearFlash();
        }
    }
}
