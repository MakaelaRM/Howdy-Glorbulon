using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Everything the Queen Nibbler throws at the player: ground warning markers, goo balls, goo puddles,
// shockwave rings, plus the shared "hurt the player" helper. All of it is made in code, so there's
// nothing to set up. QueenNibblerBoss creates these; you don't add them to anything yourself.

/// Hurts the player the same way enemies do (shield first, then health), with a red flash, a push
/// away from the hit, and a short moment of invulnerability so one attack can't hit twice.
public static class BossDamage
{
    static float invulnerableUntil;
    static MonoBehaviour runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { invulnerableUntil = 0f; runner = null; }

    /// Returns true if the hit landed.
    public static bool HitPlayer(float damage, Vector3 from, float knockback, float invulnerableTime = 0.5f)
    {
        PlayerStats stats = PlayerStats.Instance;
        if (stats == null || stats.IsDead || damage <= 0f) return false;
        if (Time.time < invulnerableUntil) return false;
        invulnerableUntil = Time.time + invulnerableTime;

        // red flash + hurt sound from the player's own hit reaction, without its small push
        PlayerHitReaction reaction = stats.GetComponent<PlayerHitReaction>();
        if (reaction != null)
        {
            float keep = reaction.knockback;
            reaction.knockback = 0f;
            reaction.TakeHit(null);
            reaction.knockback = keep;
        }

        // same rules as PlayerStats.TakeDamage
        if (stats.Shield > 0f)
        {
            if (damage <= stats.Shield) stats.SetShield(stats.Shield - damage);
            else stats.SetShield(0f);
        }
        else if (damage < stats.Health) stats.SetHealth(stats.Health - damage);
        else stats.Die();

        if (knockback > 0f && !stats.IsDead) Push(stats.transform, stats.transform.position - from, knockback);
        return true;
    }

    /// Pushes the player along a direction (flat) over a quarter of a second.
    public static void Push(Transform player, Vector3 dir, float distance)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = -player.forward;
        if (runner == null)
        {
            var go = new GameObject("BossDamageRunner");
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<BossHazard>();   // any MonoBehaviour can run the coroutine
        }
        runner.StartCoroutine(PushRoutine(player, dir.normalized, distance));
    }

    static IEnumerator PushRoutine(Transform player, Vector3 dir, float distance)
    {
        CharacterController cc = player ? player.GetComponent<CharacterController>() : null;
        const float time = 0.25f;
        for (float t = 0f; t < time && player; t += Time.deltaTime)
        {
            float k = 1f - t / time;                     // fast at first, easing out
            Vector3 step = dir * (2f * distance / time) * k * Time.deltaTime;
            if (cc && cc.enabled) cc.Move(step); else player.position += step;
            yield return null;
        }
    }

    public static Transform Player
    {
        get { return PlayerStats.Instance ? PlayerStats.Instance.transform : null; }
    }

    /// True when the player is on the ground (not jumping), used by shockwaves you can jump over.
    public static bool PlayerGrounded(float floorY)
    {
        Transform p = Player;
        if (!p) return false;
        CharacterController cc = p.GetComponent<CharacterController>();
        if (cc && !cc.isGrounded) return false;
        return p.position.y < floorY + 0.45f;
    }
}

/// Base for every boss hazard, so the boss can clear them all when the fight resets.
public class BossHazard : MonoBehaviour
{
    public static readonly List<BossHazard> All = new List<BossHazard>();
    protected virtual void OnEnable() { All.Add(this); }
    protected virtual void OnDisable() { All.Remove(this); }

    public static void ClearAll()
    {
        for (int i = All.Count - 1; i >= 0; i--)
            if (All[i] && All[i].GetType() != typeof(BossHazard)) Destroy(All[i].gameObject);
    }

    // ---------------------------------------------------------------- shared materials

    static Material gooMat, spriteMat;
    static readonly Dictionary<int, Material> markerMats = new Dictionary<int, Material>();

    public static Material Goo(Color color)
    {
        if (gooMat) return gooMat;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (!sh) sh = Shader.Find("Standard");
        gooMat = new Material(sh) { name = "Boss Goo (runtime)" };
        if (gooMat.HasProperty("_BaseColor")) gooMat.SetColor("_BaseColor", color);
        gooMat.color = color;
        if (gooMat.HasProperty("_Smoothness")) gooMat.SetFloat("_Smoothness", 0.9f);
        if (gooMat.HasProperty("_EmissionColor")) { gooMat.EnableKeyword("_EMISSION"); gooMat.SetColor("_EmissionColor", color * 0.45f); }
        return gooMat;
    }

    /// See-through material for markers and rings (works in URP).
    public static Material MarkerMaterial(int kind)
    {
        if (markerMats.TryGetValue(kind, out Material m) && m) return m;
        Shader sh = Shader.Find("Sprites/Default");
        if (!sh) sh = Shader.Find("Universal Render Pipeline/Unlit");
        m = new Material(sh) { name = "Boss Marker (runtime)" };
        m.mainTexture = kind == 0 ? DiscTexture() : kind == 1 ? RectTexture() : null;
        m.renderQueue = 3100;
        markerMats[kind] = m;
        return m;
    }

    // soft disc with a bright rim
    static Texture2D DiscTexture()
    {
        const int s = 128;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s];
        float c = (s - 1) * 0.5f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Lerp(0.25f, 0.45f, r);           // see-through middle
                if (r > 0.86f) a = 0.95f;                          // bright rim
                a *= Mathf.Clamp01((1f - r) * c);                  // smooth outer edge
                px[y * s + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    // stripe for the charge path: see-through middle, bright edges
    static Texture2D RectTexture()
    {
        const int w = 64, h = 8;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < w; x++)
        {
            float u = Mathf.Abs(x / (w - 1f) * 2f - 1f);
            float a = u > 0.85f ? 0.9f : 0.3f;
            for (int y = 0; y < h; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }
}

/// Red warning on the floor: a disc where something will land, or a stripe along a charge path.
public class BossMarker : BossHazard
{
    Renderer rend;
    Color color;
    float life, age, grow;
    Vector3 fullScale;
    MaterialPropertyBlock block;

    public static BossMarker Disc(Vector3 at, float radius, Color color, float life, float grow = 0.25f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(go.GetComponent<Collider>());
        go.name = "BossMarker";
        go.transform.position = at + Vector3.up * 0.03f;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var m = go.AddComponent<BossMarker>();
        m.Init(MarkerMaterial(0), color, life, new Vector3(radius * 2f, radius * 2f, 1f), grow);
        return m;
    }

    public static BossMarker Stripe(Vector3 from, Vector3 dir, float length, float width, Color color, float life)
    {
        dir.y = 0f; dir.Normalize();
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(go.GetComponent<Collider>());
        go.name = "BossMarker";
        go.transform.position = from + dir * (length * 0.5f) + Vector3.up * 0.03f;
        go.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);   // lies flat, long side along dir
        var m = go.AddComponent<BossMarker>();
        m.Init(MarkerMaterial(1), color, life, new Vector3(width, length, 1f), 0.15f);
        return m;
    }

    void Init(Material mat, Color c, float lifeTime, Vector3 scale, float growTime)
    {
        rend = GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        block = new MaterialPropertyBlock();
        color = c; life = lifeTime; fullScale = scale; grow = growTime;
        transform.localScale = scale * 0.2f;
        Apply(0f);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        float g = grow > 0f ? Mathf.Clamp01(age / grow) : 1f;
        transform.localScale = Vector3.Lerp(fullScale * 0.2f, fullScale, 1f - (1f - g) * (1f - g));
        // pulse faster as the hit gets close
        float k = age / life;
        float pulse = 0.65f + 0.35f * Mathf.Sin(age * Mathf.Lerp(8f, 26f, k));
        Apply(pulse);
    }

    void Apply(float a)
    {
        Color c = color; c.a *= a;
        block.SetColor("_Color", c);
        rend.SetPropertyBlock(block);
    }
}

/// Goo ball: flies in an arc to a spot, hurts the player if they're on it, and leaves a puddle.
public class BossGooBall : BossHazard
{
    Vector3 from, to;
    float time, height, age, damage, hitRadius, puddleRadius, puddleTime, puddleDamage, floorY;
    Color gooColor;
    Vector3 spin;

    public static BossGooBall Throw(Vector3 from, Vector3 to, float flightTime, float arcHeight, float size, Color goo,
                                    float damage, float hitRadius, float puddleRadius, float puddleTime, float puddleDamage, float floorY)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "BossGooBall";
        go.transform.position = from;
        go.transform.localScale = Vector3.one * size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Goo(goo);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var b = go.AddComponent<BossGooBall>();
        b.from = from; b.to = to; b.time = Mathf.Max(0.05f, flightTime); b.height = arcHeight;
        b.damage = damage; b.hitRadius = hitRadius; b.puddleRadius = puddleRadius; b.puddleTime = puddleTime;
        b.puddleDamage = puddleDamage; b.gooColor = goo; b.floorY = floorY;
        b.spin = Random.onUnitSphere * 360f;
        return b;
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = Mathf.Clamp01(age / time);
        Vector3 p = Vector3.Lerp(from, to, k) + Vector3.up * (4f * k * (1f - k) * height);
        transform.position = p;
        transform.Rotate(spin * Time.deltaTime);
        // wobble like a blob
        float w = 1f + 0.12f * Mathf.Sin(age * 25f);
        transform.localScale = new Vector3(transform.localScale.x, transform.localScale.x * w, transform.localScale.x);
        if (k >= 1f) Land();
    }

    void Land()
    {
        Transform player = BossDamage.Player;
        if (player)
        {
            Vector3 d = player.position - to; d.y = 0f;
            if (d.magnitude < hitRadius && player.position.y < to.y + 1.6f)
                BossDamage.HitPlayer(damage, to, 1.2f);
        }
        if (puddleTime > 0f && puddleRadius > 0f)
            BossGooPuddle.Make(new Vector3(to.x, floorY, to.z), puddleRadius, puddleTime, puddleDamage, gooColor, floorY);
        Splat(to, gooColor, 10, transform.localScale.x);
        Destroy(gameObject);
    }

    /// Little goo drops flying out of an impact.
    public static void Splat(Vector3 at, Color goo, int count, float size)
    {
        for (int i = 0; i < count; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = "BossGooDrop";
            g.GetComponent<Renderer>().sharedMaterial = Goo(goo);
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.transform.position = at + Vector3.up * 0.1f;
            g.transform.localScale = Vector3.one * size * Random.Range(0.18f, 0.35f);
            Vector3 v = Random.onUnitSphere; v.y = Mathf.Abs(v.y) + 0.6f;
            var drop = g.AddComponent<BossGooDrop>();
            drop.velocity = v.normalized * Random.Range(2f, 5f);
            drop.floorY = at.y;
        }
    }
}

/// One flying drop of goo that splats and fades (decoration only).
public class BossGooDrop : BossHazard
{
    public Vector3 velocity;
    public float floorY;
    float age;
    bool landed;
    Vector3 size;

    void Start() { size = transform.localScale; }

    void Update()
    {
        age += Time.deltaTime;
        if (!landed)
        {
            velocity += Physics.gravity * Time.deltaTime;
            Vector3 p = transform.position + velocity * Time.deltaTime;
            if (p.y <= floorY + 0.02f)
            {
                p.y = floorY + 0.02f;
                landed = true;
                age = 0f;
                transform.localScale = new Vector3(size.x * 2.2f, size.y * 0.2f, size.z * 2.2f);
                size = transform.localScale;
            }
            transform.position = p;
            if (age > 3f) Destroy(gameObject);
        }
        else
        {
            if (age > 1.5f) transform.localScale = size * Mathf.Clamp01(1f - (age - 1.5f) / 0.6f);
            if (age > 2.1f) Destroy(gameObject);
        }
    }
}

/// Goo puddle on the floor that hurts the player while they stand in it, then dries up.
public class BossGooPuddle : BossHazard
{
    float radius, life, damage, age, floorY, nextTick;
    Vector3 full;

    public static BossGooPuddle Make(Vector3 at, float radius, float life, float damagePerTick, Color goo, float floorY)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "BossGooPuddle";
        go.transform.position = at + Vector3.up * 0.02f;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Goo(goo);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var p = go.AddComponent<BossGooPuddle>();
        p.radius = radius; p.life = life; p.damage = damagePerTick; p.floorY = floorY;
        p.full = new Vector3(radius * 2f, 0.08f, radius * 2f * Random.Range(0.8f, 1f));
        go.transform.localScale = p.full * 0.3f;
        return p;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        float grow = Mathf.Clamp01(age / 0.15f);
        float shrink = age > life - 0.6f ? (life - age) / 0.6f : 1f;
        transform.localScale = Vector3.Lerp(full * 0.3f, full, grow) * Mathf.Max(0.05f, shrink);

        if (damage <= 0f || Time.time < nextTick) return;
        Transform player = BossDamage.Player;
        if (!player) return;
        Vector3 d = player.position - transform.position; d.y = 0f;
        if (d.magnitude < radius * 0.9f * shrink && player.position.y < floorY + 0.5f)
        {
            if (BossDamage.HitPlayer(damage, transform.position, 0f, 0.1f)) nextTick = Time.time + 0.75f;
        }
    }
}

/// Expanding ring along the floor. Jump over it: it only hurts a player standing on the ground.
public class BossShockwave : BossHazard
{
    LineRenderer line;
    Vector3 center;
    float maxRadius, speed, damage, knockback, thickness, radius, floorY;
    bool hit;
    const int Segments = 64;

    public static BossShockwave Make(Vector3 at, float maxRadius, float speed, float damage, float knockback, Color color, float floorY)
    {
        var go = new GameObject("BossShockwave");
        var s = go.AddComponent<BossShockwave>();
        s.center = new Vector3(at.x, floorY + 0.08f, at.z);
        s.maxRadius = maxRadius; s.speed = speed; s.damage = damage; s.knockback = knockback; s.floorY = floorY;
        s.thickness = 0.55f;
        s.line = go.AddComponent<LineRenderer>();
        s.line.useWorldSpace = true;
        s.line.loop = true;
        s.line.positionCount = Segments;
        s.line.sharedMaterial = MarkerMaterial(2);
        s.line.startColor = s.line.endColor = color;
        s.line.widthMultiplier = 0.35f;
        s.line.numCapVertices = 0;
        s.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        s.line.receiveShadows = false;
        s.radius = 0.3f;
        s.Draw();
        return s;
    }

    void Update()
    {
        radius += speed * Time.deltaTime;
        if (radius >= maxRadius) { Destroy(gameObject); return; }
        float fade = 1f - Mathf.Clamp01((radius - maxRadius * 0.7f) / (maxRadius * 0.3f));
        Color c = line.startColor; c.a = fade; line.startColor = line.endColor = c;
        line.widthMultiplier = Mathf.Lerp(0.45f, 0.2f, radius / maxRadius);
        Draw();

        if (hit) return;
        Transform player = BossDamage.Player;
        if (!player) return;
        Vector3 d = player.position - center; d.y = 0f;
        if (Mathf.Abs(d.magnitude - radius) < thickness && BossDamage.PlayerGrounded(floorY))
        {
            if (damage > 0f) hit = BossDamage.HitPlayer(damage, center, knockback);
            else { BossDamage.Push(player, d, knockback); hit = true; }
        }
    }

    void Draw()
    {
        for (int i = 0; i < Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            line.SetPosition(i, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
    }
}
