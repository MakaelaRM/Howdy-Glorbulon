using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// Nibbler egg nest: keeps hatching enemies until the player destroys it.
///
/// Setup:
/// 1. Put this on your egg cluster in the scene (the Mine_EggCluster_Intact model, or a parent of it).
/// 2. Make sure it has a collider (a Box Collider around the eggs is fine). Not a trigger.
/// 3. Drag your Nibbler prefab(s) into Enemy Prefabs.
/// 4. Optional: drag a Mine_EggCluster_Cracked model (placed in the same spot) into Cracked. It's shown after the break.
///
/// The player's sword and blaster damage it (MeleeAttack.cs and BlasterAttack.cs have a small hook for this).
[DisallowMultipleComponent]
public class EggNest : MonoBehaviour
{
    [Header("Models")]
    [Tooltip("The intact eggs. Leave empty to use this object's own model.")]
    public GameObject intact;
    [Tooltip("Optional: the cracked eggs model, shown after the nest is destroyed. Starts hidden.")]
    public GameObject cracked;

    [Header("Hitbox")]
    [Tooltip("Size the Box Collider to fit the eggs automatically when the game starts.")]
    public bool autoHitbox = true;
    [Tooltip("How much bigger than the eggs the hitbox is (1 = exact fit).")]
    [Range(1f, 2f)] public float hitboxPadding = 1.3f;
    [Tooltip("Minimum hitbox height in metres, so straight shots at chest height still hit low eggs.")]
    public float minHitboxHeight = 1.1f;

    [Header("Health")]
    public float maxHealth = 120f;
    [Tooltip("Shots and swings auto-aim at the nest like they do at enemies.")]
    public bool autoAim = true;
    [Tooltip("Show the enemy-style ring under the nest. Off, so players have to discover they can destroy nests.")]
    public bool showAimRing = false;

    [Header("Spawning")]
    [Tooltip("Enemies to hatch. One is picked at random each time.")]
    public List<GameObject> enemyPrefabs = new List<GameObject>();
    [Tooltip("Seconds between hatches.")]
    public float spawnInterval = 6f;
    [Tooltip("Seconds before the first hatch once the player is close.")]
    public float firstSpawnDelay = 1.5f;
    [Tooltip("Most enemies from this nest alive at once. It waits when this many are out.")]
    public int maxAlive = 3;
    [Tooltip("Only hatch while the player is this close (0 = always).")]
    public float activationRange = 16f;
    [Tooltip("How far from the nest's centre enemies appear.")]
    public float spawnRadius = 1.2f;
    [Tooltip("Optional parent for spawned enemies.")]
    public Transform spawnParent;
    [Tooltip("Kill the enemies still alive from this nest when it's destroyed.")]
    public bool killSpawnedOnDestroy = false;

    [Header("Animation")]
    [Tooltip("How hard the eggs squash and wobble when hit.")]
    [Range(0f, 0.4f)] public float hitWobble = 0.18f;
    [Tooltip("How far the eggs jolt away from the hit, in metres.")]
    [Range(0f, 0.3f)] public float hitJolt = 0.08f;
    [Tooltip("Seconds the eggs wobble before an enemy hatches (warns the player).")]
    public float hatchWarning = 0.6f;
    [Tooltip("Number of shell pieces that fly out when destroyed.")]
    public int shellPieces = 28;
    [Tooltip("Colour of the goo splash when destroyed.")]
    public Color gooColor = new Color(0.55f, 0.95f, 0.35f);
    [Tooltip("Goo blobs that splatter out each time the nest is hit.")]
    public int gooPerHit = 7;
    [Tooltip("Goo blobs that explode out when the nest is destroyed.")]
    public int gooOnBreak = 60;
    [Tooltip("Seconds goo puddles stay on the floor before drying up.")]
    public float puddleTime = 5f;
    [Tooltip("Flash of light when destroyed.")]
    public bool breakFlash = true;

    [Header("Sounds (optional)")]
    public AudioClip hitSound;
    public AudioClip hatchSound;
    public AudioClip breakSound;

    [Header("Events")]
    public UnityEvent<GameObject> onHatched;
    public UnityEvent onDestroyed;

    public bool IsDestroyed { get; private set; }
    public float Health { get; private set; }

    readonly List<GameObject> spawned = new List<GameObject>();
    Transform model;
    Vector3 modelScale, modelPos;
    float nextSpawn = -1f;
    float wobble;          // hit wobble 0..1
    bool hatching;
    Vector3 joltDir;
    Coroutine hatchRoutine;
    Transform player;
    AudioSource audioSource;
    AimTarget aimTarget;
    Material gooMat;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        Health = maxHealth;

        // Models dragged in from the Project window are files, not objects in the scene. Changing a file
        // at runtime hides it everywhere, so: Intact must be in the scene, and a Cracked file gets its own copy here.
        if (intact && !intact.scene.IsValid())
        {
            Debug.LogWarning($"[EggNest] {name}: 'Intact' points to a file in the Project window. Using this object's own model instead. " +
                             "Leave Intact empty, or drag the eggs from the Hierarchy.", this);
            intact = null;
        }
        if (cracked && !cracked.scene.IsValid())
        {
            cracked = Instantiate(cracked, transform.position, transform.rotation, transform.parent);
            cracked.transform.localScale = transform.localScale;
            cracked.name = name + "_Cracked";
            foreach (Collider c in cracked.GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        model = intact ? intact.transform : transform;
        if (autoHitbox) FitHitbox();
        modelScale = model.localScale;
        modelPos = model.localPosition;
        if (cracked) cracked.SetActive(false);

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning($"[EggNest] {name} has no collider, so attacks can't hit it. Add a Box Collider.", this);

        audioSource = GetComponent<AudioSource>();
        if (!audioSource && (hitSound || hatchSound || breakSound))
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
            audioSource.playOnAwake = false;
        }

        if (autoAim)
        {
            aimTarget = GetComponent<AimTarget>();
            if (!aimTarget)
            {
                aimTarget = gameObject.AddComponent<AimTarget>();
                Bounds b = WorldBounds();
                aimTarget.aimHeight = Mathf.Max(0.2f, b.center.y - transform.position.y);
                aimTarget.ringRadius = Mathf.Max(0.4f, Mathf.Max(b.extents.x, b.extents.z) * 0.9f);
            }
            aimTarget.showRing = showAimRing;
        }
    }

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p) player = p.transform;
    }

    // ------------------------------------------------------------------ damage

    /// Damage the nest. Called by the player's attacks.
    public void TakeDamage(float amount, Vector3 from)
    {
        if (IsDestroyed || amount <= 0f) return;
        Health -= amount;
        wobble = 1f;
        Vector3 away = transform.position - from; away.y = 0f;
        joltDir = away.sqrMagnitude > 0.001f ? away.normalized : Vector3.zero;
        HitSplatter(from);
        Play(hitSound);
        if (Health <= 0f) Break();
    }

    /// Destroy the nest right away (with the animation).
    public void Break()
    {
        if (IsDestroyed) return;
        IsDestroyed = true;
        Health = 0f;
        if (hatchRoutine != null) StopCoroutine(hatchRoutine);   // stop a hatch in progress
        hatching = false;
        if (aimTarget) aimTarget.enabled = false;               // no more aim ring / auto-aim
        StartCoroutine(BreakAnimation());

        if (killSpawnedOnDestroy)
            foreach (GameObject e in spawned)
            {
                if (!e) continue;
                EnemyStats stats = e.GetComponent<EnemyStats>();
                if (stats && !stats.IsDead) stats.Die(); else if (!stats) Destroy(e);
            }

        onDestroyed.Invoke();
    }

    // ------------------------------------------------------------------ spawning

    void Update()
    {
        AnimateWobble();
        if (IsDestroyed || hatching || enemyPrefabs.Count == 0) return;

        bool near = activationRange <= 0f || (player && (player.position - transform.position).sqrMagnitude <= activationRange * activationRange);
        if (!near) { nextSpawn = -1f; return; }
        if (nextSpawn < 0f) nextSpawn = Time.time + firstSpawnDelay;

        if (Time.time >= nextSpawn && AliveCount() < maxAlive)
            hatchRoutine = StartCoroutine(Hatch());
    }

    int AliveCount()
    {
        spawned.RemoveAll(e => e == null);
        int alive = 0;
        foreach (GameObject e in spawned)
        {
            if (!e.activeInHierarchy) continue;
            EnemyStats stats = e.GetComponent<EnemyStats>();
            if (stats && stats.IsDead) continue;
            alive++;
        }
        return alive;
    }

    IEnumerator Hatch()
    {
        hatching = true;

        // warning wobble
        for (float t = 0f; t < hatchWarning; t += Time.deltaTime)
        {
            float k = t / hatchWarning;
            float shake = Mathf.Sin(t * 45f) * 0.035f * k;
            model.localPosition = modelPos + new Vector3(shake, 0f, -shake * 0.6f);
            model.localScale = Vector3.Scale(modelScale, new Vector3(1f + 0.05f * k, 1f - 0.04f * k, 1f + 0.05f * k));
            if (IsDestroyed) yield break;
            yield return null;
        }
        model.localPosition = modelPos;
        model.localScale = modelScale;
        wobble = 0.8f;

        GameObject prefab = PickPrefab();
        if (prefab)
        {
            Vector3 pos = FindSpawnPoint(out float a);
            Quaternion rot = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            GameObject e = Instantiate(prefab, pos, rot, spawnParent);
            Prepare(e);
            spawned.Add(e);
            Play(hatchSound);
            SpawnShell(pos + Vector3.up * 0.3f, 8, 0.6f);   // shell bits burst where it hatches
            onHatched.Invoke(e);
        }

        nextSpawn = Time.time + spawnInterval;
        hatching = false;
    }

    // A spot on the floor next to the nest: same height as the nest's base, not on top of rocks,
    // enemies or other nests, and not inside a wall. Tries a few directions.
    Vector3 FindSpawnPoint(out float angle)
    {
        float baseY = transform.position.y;
        Vector3 fallback = transform.position;
        angle = Random.value * Mathf.PI * 2f;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = angle + attempt * Mathf.PI * 2f / 8f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 xz = transform.position + dir * spawnRadius;
            if (attempt == 0) fallback = new Vector3(xz.x, baseY, xz.z);

            // find the floor: the hit closest to the nest's base height, ignoring nests, enemies and the player
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(xz.x, baseY + 1.5f, xz.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            bool found = false; float floorY = baseY; float best = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                if (IsIgnored(h.collider)) continue;
                float diff = Mathf.Abs(h.point.y - baseY);
                if (diff < best) { best = diff; floorY = h.point.y; found = true; }
            }
            if (!found || best > 0.35f) continue;           // no floor near nest level here (a ledge, rock or hole)

            // room to stand: nothing solid where the enemy's body would be
            Vector3 body = new Vector3(xz.x, floorY + 0.45f, xz.z);
            bool blocked = false;
            foreach (Collider c in Physics.OverlapSphere(body, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                if (!IsIgnored(c)) { blocked = true; break; }
            if (blocked) continue;

            angle = a;
            return new Vector3(xz.x, floorY, xz.z);
        }
        return fallback;   // nothing good found: right beside the nest at its base height
    }

    bool IsIgnored(Collider c)
    {
        if (c.transform.IsChildOf(transform)) return true;                       // this nest
        if (cracked && c.transform.IsChildOf(cracked.transform)) return true;
        if (c.GetComponentInParent<EggNest>()) return true;                     // other nests
        if (c.GetComponentInParent<AimTarget>()) return true;                   // enemies
        if (c.GetComponentInParent<CharacterController>()) return true;         // player
        return false;
    }

    // Same setup the BoxTriggerSpawner does: stats that don't respawn (the nest is the respawner), and aim ring.
    static void Prepare(GameObject e)
    {
        EnemyStats stats = e.GetComponent<EnemyStats>();
        if (stats == null && e.GetComponent<AlienWildWest.PestChomper>() != null)
            stats = e.AddComponent<EnemyStats>();
        if (stats != null) stats.ShouldRespawn = false;
        if (e.GetComponent<AimTarget>() == null) e.AddComponent<AimTarget>();
    }

    GameObject PickPrefab()
    {
        var valid = enemyPrefabs.FindAll(p => p != null);
        return valid.Count > 0 ? valid[Random.Range(0, valid.Count)] : null;
    }

    // ------------------------------------------------------------------ animation

    void AnimateWobble()
    {
        if (IsDestroyed || hatching || wobble <= 0f) return;
        wobble = Mathf.Max(0f, wobble - Time.deltaTime * 3f);
        float s = Mathf.Sin(wobble * Mathf.PI * 3f) * hitWobble * wobble;
        model.localScale = Vector3.Scale(modelScale, new Vector3(1f + s, 1f - s, 1f + s));

        // jolt away from the hit and spring back
        Vector3 jolt = joltDir * hitJolt * Mathf.Sin(wobble * Mathf.PI) * wobble;
        Vector3 joltLocal = model.parent ? model.parent.InverseTransformVector(jolt) : jolt;
        model.localPosition = modelPos + joltLocal;
        if (wobble <= 0f) model.localPosition = modelPos;
    }

    IEnumerator BreakAnimation()
    {
        Play(breakSound);

        // 1. violent shake and swell
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            float k = t / 0.35f;
            float shake = Mathf.Sin(t * 70f) * 0.06f;
            model.localPosition = modelPos + new Vector3(shake, 0f, shake * 0.7f);
            model.localScale = modelScale * (1f + 0.12f * k);
            yield return null;
        }

        // 2. burst: shell pieces, goo, flash
        Bounds b = WorldBounds();
        SpawnShell(b.center, shellPieces, 1f);
        SpawnGoo(b.center, gooOnBreak, Vector3.zero, 1.6f, 1.4f);                      // big explosion of goo
        SpawnGoo(b.center + Vector3.up * 0.3f, gooOnBreak / 3, Vector3.up, 2.2f, 0.8f); // and a fountain upward
        if (breakFlash) StartCoroutine(Flash(b.center));

        // 3. swap to the cracked model (with a small squash), or shrink the eggs away
        model.localPosition = modelPos;
        if (cracked)
        {
            if (intact && intact != gameObject) intact.SetActive(false);
            else foreach (Renderer r in GetComponentsInChildren<Renderer>()) if (!r.transform.IsChildOf(cracked.transform)) r.enabled = false;
            cracked.SetActive(true);
            Transform c = cracked.transform;
            Vector3 full = c.localScale;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                float k = t / 0.3f;
                c.localScale = Vector3.Scale(full, new Vector3(1f + 0.15f * (1f - k), 1f - 0.25f * (1f - k), 1f + 0.15f * (1f - k)));
                yield return null;
            }
            c.localScale = full;
        }
        else
        {
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                model.localScale = Vector3.Scale(modelScale, new Vector3(1.15f, Mathf.Lerp(1.1f, 0.15f, t / 0.25f), 1.15f));
                yield return null;
            }
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        // no longer blocks bullets or the player once it's just rubble
        foreach (Collider col in GetComponentsInChildren<Collider>())
            if (!cracked || !col.transform.IsChildOf(cracked.transform)) col.enabled = false;
    }

    // Shell pieces use the eggs' own materials, so they match the speckled colours.
    void SpawnShell(Vector3 at, int count, float power)
    {
        Renderer[] rs = model.GetComponentsInChildren<Renderer>();
        var mats = new List<Material>();
        foreach (Renderer r in rs) foreach (Material m in r.sharedMaterials) if (m) mats.Add(m);

        for (int i = 0; i < count; i++)
        {
            GameObject piece = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Quad : PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            piece.name = "EggShell";
            var pr = piece.GetComponent<Renderer>();
            if (mats.Count > 0) pr.sharedMaterial = mats[Random.Range(0, mats.Count)];
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float size = Random.Range(0.06f, 0.16f) * Mathf.Lerp(0.7f, 1f, power);
            piece.transform.localScale = new Vector3(size, size * Random.Range(0.25f, 0.6f), size * Random.Range(0.6f, 1f));
            piece.transform.position = at + Random.insideUnitSphere * 0.25f;
            piece.transform.rotation = Random.rotation;

            Vector3 dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 1.4f + 0.3f;
            StartCoroutine(Fly(piece.transform, dir.normalized * Random.Range(2.5f, 5.5f) * power, Random.Range(0.8f, 1.4f), true));
        }
    }

    // Small splash of goo on the side that got hit, spraying back toward the attacker.
    void HitSplatter(Vector3 from)
    {
        if (gooPerHit <= 0) return;
        Bounds b = WorldBounds();
        Vector3 toAttacker = from - b.center; toAttacker.y = 0f;
        toAttacker = toAttacker.sqrMagnitude > 0.001f ? toAttacker.normalized : Random.onUnitSphere;
        Vector3 at = b.center + toAttacker * Mathf.Max(b.extents.x, b.extents.z) * 0.8f;
        Collider col = GetComponentInChildren<Collider>();
        if (col && col.enabled) at = col.ClosestPoint(from + Vector3.up * (b.center.y - from.y));
        SpawnGoo(at, gooPerHit, toAttacker, 0.9f, 0.7f);
    }

    Material GooMaterial()
    {
        if (!gooMat)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) sh = Shader.Find("Standard");
            gooMat = new Material(sh);
            if (gooMat.HasProperty("_BaseColor")) gooMat.SetColor("_BaseColor", gooColor);
            gooMat.color = gooColor;
            if (gooMat.HasProperty("_Smoothness")) gooMat.SetFloat("_Smoothness", 0.9f);
            if (gooMat.HasProperty("_EmissionColor")) { gooMat.EnableKeyword("_EMISSION"); gooMat.SetColor("_EmissionColor", gooColor * 0.35f); }
        }
        return gooMat;
    }

    /// Goo blobs. bias: direction they mostly fly (zero = all around). power: speed. size: blob size.
    void SpawnGoo(Vector3 at, int count, Vector3 bias, float power, float size)
    {
        Material mat = GooMaterial();
        for (int i = 0; i < count; i++)
        {
            GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(blob.GetComponent<Collider>());
            blob.name = "EggGoo";
            var r = blob.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float s = Random.Range(0.05f, 0.14f) * size;
            blob.transform.localScale = new Vector3(s, s * Random.Range(0.7f, 1.1f), s);
            blob.transform.position = at + Random.insideUnitSphere * 0.15f;

            Vector3 dir = Random.onUnitSphere;
            if (bias != Vector3.zero) dir = (bias * 1.4f + dir * 0.8f);
            dir.y = Mathf.Abs(dir.y) + 0.6f;
            StartCoroutine(GooFly(blob.transform, dir.normalized * Random.Range(1.2f, 3.2f) * power));
        }
    }

    // Goo flies, stretches along its path, splats flat where it lands and leaves a puddle that dries up.
    IEnumerator GooFly(Transform t, Vector3 v)
    {
        float floor = transform.position.y + 0.015f;
        Vector3 full = t.localScale;
        for (float age = 0f; age < 2f && t; age += Time.deltaTime)
        {
            v += Physics.gravity * Time.deltaTime;
            Vector3 p = t.position + v * Time.deltaTime;
            if (p.y <= floor)
            {
                p.y = floor;
                t.position = p;
                StartCoroutine(Puddle(t, full.x));
                yield break;
            }
            t.position = p;
            // stretch in the direction it's flying, like a drop
            float stretch = Mathf.Clamp(v.magnitude * 0.12f, 1f, 1.8f);
            t.rotation = Quaternion.FromToRotation(Vector3.up, v.normalized);
            t.localScale = new Vector3(full.x / Mathf.Sqrt(stretch), full.y * stretch, full.z / Mathf.Sqrt(stretch));
            yield return null;
        }
        if (t) Destroy(t.gameObject);
    }

    IEnumerator Puddle(Transform t, float size)
    {
        t.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        float target = size * Random.Range(1.8f, 3f);
        Vector3 flat = new Vector3(target, size * 0.12f, target * Random.Range(0.7f, 1f));
        // splat out quickly
        for (float k = 0f; k < 1f && t; k += Time.deltaTime / 0.12f)
        {
            t.localScale = Vector3.Lerp(new Vector3(size, size * 0.5f, size), flat, 1f - (1f - k) * (1f - k));
            yield return null;
        }
        if (t) t.localScale = flat;
        // stay a while, then dry up
        yield return new WaitForSeconds(puddleTime * Random.Range(0.8f, 1.2f));
        for (float k = 0f; k < 1f && t; k += Time.deltaTime / 0.8f)
        {
            t.localScale = new Vector3(flat.x * (1f - k), flat.y * (1f - k * 0.5f), flat.z * (1f - k));
            yield return null;
        }
        if (t) Destroy(t.gameObject);
    }

    // Simple physics: gravity, bounce off the floor height, spin, then shrink away.
    IEnumerator Fly(Transform t, Vector3 v, float life, bool spin)
    {
        float floor = transform.position.y + 0.02f;
        Vector3 spinAxis = Random.onUnitSphere;
        float spinSpeed = Random.Range(360f, 900f);
        Vector3 full = t.localScale;
        for (float age = 0f; age < life && t; age += Time.deltaTime)
        {
            v += Physics.gravity * Time.deltaTime;
            Vector3 p = t.position + v * Time.deltaTime;
            if (p.y < floor)
            {
                p.y = floor;
                v = new Vector3(v.x * 0.5f, -v.y * 0.3f, v.z * 0.5f);   // bounce
                spinSpeed *= 0.5f;
                if (!spin) t.localScale = Vector3.Scale(t.localScale, new Vector3(1.05f, 0.85f, 1.05f));   // goo splats
            }
            t.position = p;
            if (spin) t.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
            float k = age / life;
            if (k > 0.65f) t.localScale = full * (1f - (k - 0.65f) / 0.35f);
            yield return null;
        }
        if (t) Destroy(t.gameObject);
    }

    IEnumerator Flash(Vector3 at)
    {
        var go = new GameObject("EggBreakFlash");
        go.transform.position = at + Vector3.up * 0.4f;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Color.Lerp(gooColor, Color.white, 0.4f);
        l.range = 6f;
        l.shadows = LightShadows.None;
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            l.intensity = Mathf.Lerp(6f, 0f, t / 0.4f);
            yield return null;
        }
        Destroy(go);
    }

    // ------------------------------------------------------------------ helpers

    // Box Collider on this object that wraps all the egg meshes, padded, and tall enough to catch level shots.
    void FitHitbox()
    {
        bool any = false;
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh || (cracked && mf.transform.IsChildOf(cracked.transform))) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                min = Vector3.Min(min, local); max = Vector3.Max(max, local);
                any = true;
            }
        }
        if (!any) return;

        BoxCollider box = GetComponent<BoxCollider>();
        if (!box) box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = false;

        Vector3 size = (max - min);
        Vector3 center = (max + min) * 0.5f;
        size.x *= hitboxPadding; size.z *= hitboxPadding; size.y *= Mathf.Lerp(1f, hitboxPadding, 0.5f);

        // make it at least minHitboxHeight tall (in world metres), growing upward from the bottom
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        float minLocal = minHitboxHeight / scaleY;
        float bottom = min.y;
        if (size.y < minLocal) size.y = minLocal;
        center.y = bottom + size.y * 0.5f;

        box.center = center;
        box.size = size;
    }

    void Play(AudioClip clip) { if (clip && audioSource) audioSource.PlayOneShot(clip); }

    Bounds WorldBounds()
    {
        Renderer[] rs = (intact ? intact : gameObject).GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(transform.position + Vector3.up * 0.4f, Vector3.one);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    void OnDestroy() { if (gooMat) Destroy(gooMat); }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.55f, 0.95f, 0.35f, 1f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
        if (activationRange > 0f)
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, activationRange);
        }
    }

    // ------------------------------------------------------------------ blaster hook

    /// Called by BlasterAttack when it fires: works out whether the bullet will hit a nest,
    /// and damages it when the bullet gets there (bullets fly straight and nests don't move).
    public static void ReportShot(Vector3 origin, Vector3 dir, float maxDistance, float radius, LayerMask layers,
                                  float speed, float damage, Transform shooter)
    {
        // the player's own colliders (the gun can be a child of the player, so use the player's root)
        Transform owner = shooter;
        if (shooter)
        {
            CharacterController cc = shooter.GetComponentInParent<CharacterController>();
            if (cc) owner = cc.transform;
        }

        RaycastHit[] hits = Physics.SphereCastAll(origin, Mathf.Max(0.01f, radius), dir, maxDistance, layers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            if (owner && h.collider.transform.IsChildOf(owner)) continue;
            EggNest nest = h.collider.GetComponentInParent<EggNest>();
            if (nest == null)
            {
                // something else is first. If it's just the floor (or a pebble) right in front of a nest, still count it.
                nest = NestJustBehind(hits, h.distance + 0.4f, owner);
                if (nest == null) return;
            }
            if (nest.IsDestroyed) return;
            float delay = speed > 0f ? h.distance / speed : 0f;
            nest.StartCoroutine(nest.DelayedDamage(delay, damage, origin));
            return;
        }
    }

    static EggNest NestJustBehind(RaycastHit[] sorted, float within, Transform owner)
    {
        foreach (RaycastHit h in sorted)
        {
            if (h.distance > within) break;
            if (owner && h.collider.transform.IsChildOf(owner)) continue;
            EggNest n = h.collider.GetComponentInParent<EggNest>();
            if (n != null) return n;
        }
        return null;
    }

    IEnumerator DelayedDamage(float delay, float damage, Vector3 from)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        TakeDamage(damage, from);
    }
}