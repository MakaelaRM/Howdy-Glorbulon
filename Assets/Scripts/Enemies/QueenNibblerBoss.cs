using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

/// The Queen Nibbler boss fight.
///
/// Setup:
/// 1. Drag Boss_QueenNibbler (the model) into the boss area, where she should stand.
/// 2. Add this script to it (the root object). It adds EnemyStats, EnemyHitReaction, AimTarget and a collider
///    by itself, so your sword, blaster and aim assist work on her straight away.
/// 3. Fill in Swarm Prefabs (your Nibbler prefabs), Loot Crate Prefab and Bar Font (Kleader).
/// 4. Set Arena Radius to the size of the elevated area (the orange circle in the Scene view when selected).
///
/// The fight starts when the player comes within Start Range of the arena centre.
/// Set her health and damage here, not on EnemyStats: this script copies them across.
[DisallowMultipleComponent]
public class QueenNibblerBoss : MonoBehaviour
{
    [Header("Boss")]
    public string bossName = "QUEEN NIBBLER";
    public string bossTitle = "Mother of the Mine";
    [Tooltip("Font for the health bar (Kleader).")]
    public TMP_FontAsset barFont;
    [Tooltip("Total health. Blaster ~20 per shot, sword ~40 per swing.")]
    public float maxHealth = 1500f;
    [Tooltip("Damage when the player touches her.")]
    public float contactDamage = 15f;

    [Header("Arena")]
    [Tooltip("Centre of the boss area. Leave empty to use where she's placed.")]
    public Transform arenaCenter;
    [Tooltip("She never leaves this circle (metres). Match it to the elevated area.")]
    public float arenaRadius = 10f;
    [Tooltip("The fight starts when the player is this close to the arena centre.")]
    public float startRange = 9f;
    [Tooltip("The health bar hides (and she waits) when the player is further than this.")]
    public float leaveRange = 18f;
    [Tooltip("Optional: objects switched on during the fight (e.g. goo walls blocking the way out).")]
    public GameObject[] lockWalls;
    [Tooltip("When the player dies, she heals up and the fight starts over.")]
    public bool resetWhenPlayerDies = true;
    [Tooltip("She drops in from above when the fight starts (hidden until then).")]
    public bool dropInIntro = true;

    [Header("Movement")]
    public float hopHeight = 0.8f;
    [Tooltip("Seconds per hop when she's not chasing (going home).")]
    public float hopTime = 0.75f;
    [Tooltip("Metres per hop while chasing the player. Hop length / hop time = her chase speed (player walks 4, sprints 7.2).")]
    public float chaseHopLength = 2.8f;
    [Tooltip("Seconds per hop while chasing the player.")]
    public float chaseHopSeconds = 0.42f;
    [Tooltip("She chases until she's this close to the player, then attacks.")]
    public float closeDistance = 2.2f;
    public float turnSpeed = 200f;
    [Tooltip("Squash and stretch amount.")]
    public float squash = 0.12f;

    [Header("Pacing")]
    [Tooltip("Longest she chases before attacking anyway, phase 1 (min, max seconds). She attacks sooner if she catches you.")]
    public Vector2 chaseTimePhase1 = new Vector2(0.6f, 1.2f);
    [Tooltip("Same for phase 2.")]
    public Vector2 chaseTimePhase2 = new Vector2(0.3f, 0.7f);
    [Tooltip("Phase 2 starts at this fraction of health.")]
    [Range(0.1f, 0.9f)] public float enrageAt = 0.5f;
    [Tooltip("How much faster everything is in phase 2.")]
    public float phase2Speed = 1.35f;

    [Header("Attack: Chomp (close range)")]
    [Tooltip("She uses Chomp when the player is this close.")]
    public float chompRange = 3.6f;
    [Tooltip("Seconds of wide-open jaw before the lunge (the warning).")]
    public float chompWindup = 0.35f;
    public float chompLunge = 3.2f;
    public float chompDamage = 20f;
    [Tooltip("Chomps in a row before she has to use another attack.")]
    public int maxChompsInARow = 2;

    [Header("Attack: Bounce Chase")]
    public int chaseHopsPhase1 = 3;
    public int chaseHopsPhase2 = 4;
    public float chaseHopHeight = 2.2f;
    public float chaseHopTime = 0.85f;
    public float landRadius = 2.6f;
    public float landDamage = 15f;

    [Header("Attack: Rolling Charge")]
    public float chargeWindup = 1.0f;
    public float chargeSpeed = 13f;
    public float chargeDamage = 25f;
    [Tooltip("Seconds she's dazed after rolling into the edge of the arena or a wall.")]
    public float dazeTime = 2.2f;
    [Tooltip("Damage she takes while dazed is multiplied by this.")]
    public float dazedDamageMultiplier = 1.5f;

    [Header("Attack: Goo Spit")]
    public int spitPhase1 = 5;
    public int spitPhase2 = 9;
    [Tooltip("Width of the fan, in degrees.")]
    public float spitSpread = 70f;
    public float spitFlightTime = 0.9f;
    public float spitDamage = 12f;
    public float puddleRadius = 1.1f;
    public float puddleTime = 4f;
    [Tooltip("Damage every 0.75 s while standing in goo.")]
    public float puddleDamage = 6f;

    [Header("Attack: Call the Swarm")]
    [Tooltip("Your Nibbler prefabs. Leave empty to skip this attack.")]
    public List<GameObject> swarmPrefabs = new List<GameObject>();
    public Vector2Int swarmPhase1 = new Vector2Int(2, 3);
    public Vector2Int swarmPhase2 = new Vector2Int(4, 5);
    [Tooltip("Most of her Nibblers alive at once.")]
    public int maxSwarm = 6;

    [Header("Attack: Belly Flop (phase 2)")]
    public float flopHeight = 6f;
    public float flopAirTime = 1.2f;
    public float flopRadius = 2.6f;
    public float flopDamage = 25f;
    [Tooltip("Shockwave ring the player has to jump over.")]
    public float shockwaveRadius = 9f;
    public float shockwaveSpeed = 7f;
    public float shockwaveDamage = 20f;

    [Header("Attack: Goo Rain (phase 2)")]
    public int rainDrops = 10;
    public float rainTime = 2.5f;
    [Tooltip("Seconds the warning shows before each drop lands.")]
    public float rainWarning = 0.9f;
    public float rainRadius = 1.2f;
    public float rainDamage = 15f;

    [Header("Defeat")]
    [Tooltip("Dropped where she dies (e.g. your Legendary loot crate prefab, set up with LootCrate).")]
    public GameObject lootCratePrefab;
    [Tooltip("Optional: where the crate lands. Empty = where she dies.")]
    public Transform crateSpawnPoint;
    public int explosionGoo = 140;

    [Header("Look")]
    public Color gooColor = new Color(0.55f, 0.95f, 0.35f);
    public Color warningColor = new Color(1f, 0.15f, 0.12f, 0.9f);
    public Color enragedEyeColor = new Color(1f, 0.25f, 0.2f);

    [Header("Sounds (optional)")]
    public AudioClip roarSound;
    public AudioClip landSound;
    public AudioClip spitSound;
    public AudioClip chargeSound;
    public AudioClip screechSound;
    public AudioClip defeatSound;

    [Header("Quests")]
    [Tooltip("Quest event sent when she's defeated. A quest step of type Event with this Event Id completes.")]
    public string questEventId = "QueenNibbler";

    [Header("Events")]
    public UnityEvent onFightStarted;
    public UnityEvent onPhase2;
    public UnityEvent onDefeated;

    // ------------------------------------------------------------------ state for other scripts

    public float Health => stats ? stats.Health : 0f;
    public float MaxHealth => maxHealth;
    public bool Enraged { get; private set; }
    public bool FightActive { get; private set; }
    public bool Defeated { get; private set; }
    public bool PlayerInRange { get; private set; }

    // ------------------------------------------------------------------ internals

    enum Attack { BounceChase, Charge, Spit, Swarm, BellyFlop, Rain, Chomp }

    EnemyStats stats;
    EnemyHitReaction hitReaction;
    AimTarget aimTarget;
    DeathRespawn death;
    AudioSource audioSource;
    Transform jaw, crown, player;
    Quaternion jawRest;
    Vector3 baseScale, home;
    Quaternion homeRot;
    float floorY, bodyRadius, bodyHeight;
    float yawDeg, spinDeg;
    float jawAngle, jawTarget;
    float lift;                // height above the floor (hops)
    float squashY = 1f;
    bool dazed;
    float lastHealth;
    Coroutine fight;
    Attack lastAttack = (Attack)(-1);
    int chompsInARow;
    readonly List<GameObject> swarm = new List<GameObject>();
    Renderer[] eyeRenderers; List<int>[] eyeSlots; MaterialPropertyBlock eyeBlock;
    bool hidden;

    const float JawOpen = 22f, JawShut = -10f;

    Vector3 Center => arenaCenter ? arenaCenter.position : home;

    // ================================================================== setup

    void Awake()
    {
        home = transform.position;
        homeRot = transform.rotation;
        baseScale = transform.localScale;
        floorY = home.y;
        yawDeg = transform.eulerAngles.y;

        foreach (Transform c in GetComponentsInChildren<Transform>(true))
        {
            if (c == transform) continue;
            if (!jaw && c.name.EndsWith("_Jaw")) jaw = c;
            if (!crown && c.name.EndsWith("_Crown")) crown = c;
        }
        if (jaw) jawRest = jaw.localRotation;
        else Debug.LogWarning("[QueenNibblerBoss] No child ending in _Jaw found, so she can't chomp.", this);

        MeasureBody();
        SetUpCombatComponents();
        FindEyes();

        audioSource = GetComponent<AudioSource>();
        if (!audioSource)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0.6f;
            audioSource.playOnAwake = false;
        }
    }

    void MeasureBody()
    {
        bool any = false; Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (crown && r.transform.IsChildOf(crown)) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        bodyHeight = any ? b.size.y : 2f;
        bodyRadius = any ? Mathf.Max(b.extents.x, b.extents.z) * 0.8f : 1f;

        if (!GetComponent<Collider>())
        {
            var sphere = gameObject.AddComponent<SphereCollider>();
            float scale = Mathf.Max(0.0001f, transform.lossyScale.x);
            sphere.center = transform.InverseTransformPoint(new Vector3(b.center.x, b.center.y - bodyHeight * 0.05f, b.center.z));
            sphere.radius = Mathf.Min(b.extents.x, b.extents.y, b.extents.z) * 0.92f / scale;
        }
    }

    void SetUpCombatComponents()
    {
        stats = GetComponent<EnemyStats>();
        if (!stats) stats = gameObject.AddComponent<EnemyStats>();
        // EnemyStats keeps these private, so they're copied across from this script's settings.
        SetPrivate(stats, "maxHealth", maxHealth);
        SetPrivate(stats, "damage", contactDamage);
        stats.ShouldRespawn = false;
        stats.SetHealth(maxHealth);
        lastHealth = maxHealth;

        hitReaction = GetComponent<EnemyHitReaction>();
        if (!hitReaction) hitReaction = gameObject.AddComponent<EnemyHitReaction>();
        hitReaction.stunTime = 0f;        // a boss doesn't get stunned or pushed around by hits
        hitReaction.knockback = 0f;
        hitReaction.flashTime = 0.25f;
        hitReaction.flashBlinks = 0;

        aimTarget = GetComponent<AimTarget>();
        if (!aimTarget) aimTarget = gameObject.AddComponent<AimTarget>();
        aimTarget.ringRadius = Mathf.Max(0.6f, bodyRadius);
        aimTarget.ringWidth = 0.08f;

        // Her death is played by this script. DeathRespawn only hides her at the end of it.
        death = GetComponent<DeathRespawn>();
        if (!death) death = gameObject.AddComponent<DeathRespawn>();
        death.fallAngle = 0f;
        death.fallTime = 0.01f;
        death.lieTime = 3.2f;
        death.smokeSize = 3f;
        death.smokeColor = new Color(gooColor.r, gooColor.g, gooColor.b, 0.85f);
        death.smokeOnRespawn = false;
        death.pauseWhileDead.Clear();
        death.pauseWhileDead.Add(aimTarget);          // only the aim ring switches off; this script keeps running
        death.pauseWhenGone.Clear();
        death.pauseWhenGone.Add(hitReaction);
    }

    static void SetPrivate(object target, string field, float value)
    {
        FieldInfo f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (f != null && f.FieldType == typeof(float)) f.SetValue(target, value);
        else Debug.LogWarning($"[QueenNibblerBoss] Couldn't set EnemyStats.{field}. Set it on EnemyStats by hand.");
    }

    void FindEyes()
    {
        var rs = new List<Renderer>(); var slots = new List<List<int>>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            var list = new List<int>();
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] && mats[i].name.Contains("EyeW")) list.Add(i);
            if (list.Count > 0) { rs.Add(r); slots.Add(list); }
        }
        eyeRenderers = rs.ToArray(); eyeSlots = slots.ToArray();
        eyeBlock = new MaterialPropertyBlock();
    }

    void OnEnable() { if (stats) stats.Died += OnDied; }
    void OnDisable() { if (stats) stats.Died -= OnDied; }

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p) player = p.transform;
        if (PlayerStats.Instance) PlayerStats.Instance.Died += OnPlayerDied;
        SetWalls(false);
        if (dropInIntro) SetHidden(true);
    }

    void OnDestroy()
    {
        if (PlayerStats.Instance) PlayerStats.Instance.Died -= OnPlayerDied;
        BossHealthBar.Hide(this);
    }

    // ================================================================== every frame

    void Update()
    {
        if (!player)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform; else return;
        }

        float d = FlatDistance(player.position, Center);
        PlayerInRange = d <= leaveRange;

        if (!FightActive && !Defeated && d <= startRange && PlayerAlive())
            StartFight();

        if (FightActive && !Defeated)
        {
            if (PlayerInRange) BossHealthBar.Show(this);
            else BossHealthBar.Hide(this);
        }

        // extra damage while dazed (EnemyStats is changed directly by your attacks)
        if (stats && !stats.IsDead)
        {
            float h = stats.Health;
            if (dazed && h < lastHealth && dazedDamageMultiplier > 1f)
            {
                float extra = (lastHealth - h) * (dazedDamageMultiplier - 1f);
                if (h - extra <= 0f) stats.Die();
                else stats.SetHealth(h - extra);
                h = stats.Health;
            }
            lastHealth = h;
        }
    }

    void LateUpdate()
    {
        // pose: hop height, squash, facing, roll spin, jaw
        Vector3 pos = transform.position; pos.y = floorY + lift; transform.position = pos;
        float sx = 1f / Mathf.Sqrt(Mathf.Max(0.3f, squashY));
        transform.localScale = Vector3.Scale(baseScale, new Vector3(sx, squashY, sx));
        transform.rotation = Quaternion.Euler(0f, yawDeg, 0f) * Quaternion.Euler(spinDeg, 0f, 0f);

        if (jaw)
        {
            jawAngle = Mathf.MoveTowards(jawAngle, jawTarget, 220f * Time.deltaTime);
            jaw.localRotation = jawRest * Quaternion.Euler(jawAngle, 0f, 0f);
        }

        // red eyes in phase 2 (re-applied after the hit flash)
        if (Enraged && !Defeated)
            foreach (var pair in EyeSlots())
            {
                pair.r.GetPropertyBlock(eyeBlock, pair.i);
                eyeBlock.SetColor("_BaseColor", enragedEyeColor);
                eyeBlock.SetColor("_Color", enragedEyeColor);
                pair.r.SetPropertyBlock(eyeBlock, pair.i);
            }
    }

    IEnumerable<(Renderer r, int i)> EyeSlots()
    {
        for (int k = 0; k < eyeRenderers.Length; k++)
            if (eyeRenderers[k]) foreach (int i in eyeSlots[k]) yield return (eyeRenderers[k], i);
    }

    // ================================================================== fight flow

    void StartFight()
    {
        FightActive = true;
        Enraged = false;
        SetWalls(true);
        BossHealthBar.Show(this);
        onFightStarted.Invoke();
        fight = StartCoroutine(FightLoop());
    }

    IEnumerator FightLoop()
    {
        yield return Intro();
        while (true)
        {
            if (!Enraged && stats.Health <= maxHealth * enrageAt) yield return Enrage();

            // wait (near home) while the player is out of range
            while (!PlayerInRange || !PlayerAlive())
            {
                if (FlatDistance(transform.position, home) > 1f) yield return Hop(home, hopHeight, hopTime);
                else { yield return Idle(0.5f); }
            }

            Vector2 chase = Enraged ? chaseTimePhase2 : chaseTimePhase1;
            yield return Pursue(Random.Range(chase.x, chase.y));
            if (!PlayerInRange) continue;
            if (!Enraged && stats.Health <= maxHealth * enrageAt) continue;   // enrage first

            Attack a = PickAttack();
            chompsInARow = a == Attack.Chomp ? chompsInARow + 1 : 0;
            lastAttack = a;
            switch (a)
            {
                case Attack.Chomp: yield return Chomp(); break;
                case Attack.BounceChase: yield return BounceChase(); break;
                case Attack.Charge: yield return Charge(); break;
                case Attack.Spit: yield return GooSpit(); break;
                case Attack.Swarm: yield return CallSwarm(); break;
                case Attack.BellyFlop: yield return BellyFlop(); break;
                case Attack.Rain: yield return GooRain(); break;
            }
        }
    }

    // Close: mostly bites and body-slams. Further away: attacks that reach (charge, spit, rain, belly flop).
    Attack PickAttack()
    {
        float d = player ? FlatDistance(player.position, transform.position) : 99f;
        var options = new List<Attack>();
        if (d <= chompRange)
        {
            if (chompsInARow < maxChompsInARow) { options.Add(Attack.Chomp); options.Add(Attack.Chomp); options.Add(Attack.Chomp); }
            options.Add(Attack.BounceChase);
            if (CanSwarm()) options.Add(Attack.Swarm);
            if (Enraged) { options.Add(Attack.BellyFlop); options.Add(Attack.Spit); }
        }
        else
        {
            options.Add(Attack.Charge); options.Add(Attack.Spit); options.Add(Attack.BounceChase);
            if (CanSwarm()) options.Add(Attack.Swarm);
            if (Enraged) { options.Add(Attack.BellyFlop); options.Add(Attack.Rain); options.Add(Attack.Charge); }
        }
        if (lastAttack != Attack.Chomp) options.RemoveAll(o => o == lastAttack);   // no repeats (except chomp chains)
        if (options.Count == 0) options.Add(Attack.BounceChase);
        return options[Random.Range(0, options.Count)];
    }

    float T(float seconds) => Enraged ? seconds / phase2Speed : seconds;

    // ================================================================== intro / phase change

    IEnumerator Intro()
    {
        if (dropInIntro)
        {
            // warning circle, then she falls out of the ceiling
            BossMarker.Disc(home, bodyRadius * 1.6f, warningColor, 1.0f, 0.3f);
            transform.position = new Vector3(home.x, floorY, home.z);
            lift = 14f;
            SetHidden(false);
            yield return new WaitForSeconds(0.4f);
            float v = 0f;
            while (lift > 0f)
            {
                v += 40f * Time.deltaTime;
                lift = Mathf.Max(0f, lift - v * Time.deltaTime);
                squashY = 1.15f;
                yield return null;
            }
            Land(1.0f, 0f);
            BossShockwave.Make(transform.position, 7f, 10f, 0f, 2.5f, new Color(1f, 0.85f, 0.6f, 0.9f), floorY);
            yield return SquashTo(0.7f, 0.12f);
            yield return SquashTo(1f, 0.25f);
        }
        yield return TurnToPlayer(0.4f);
        yield return Roar(1.6f);
    }

    IEnumerator Enrage()
    {
        Enraged = true;
        onPhase2.Invoke();
        yield return TurnToPlayer(0.3f);
        BossShockwave.Make(transform.position, 8f, 12f, 0f, 3f, new Color(1f, 0.3f, 0.2f, 0.95f), floorY);
        yield return Roar(2.0f);
    }

    IEnumerator Roar(float time)
    {
        Play(roarSound);
        jawTarget = JawOpen * 1.3f;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            squashY = 1.08f + Mathf.Sin(t * 50f) * 0.03f;
            yield return null;
        }
        jawTarget = 0f; squashY = 1f;
    }

    // ================================================================== moving

    IEnumerator Idle(float time)
    {
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            squashY = 1f + Mathf.Sin(Time.time * 2.5f) * 0.03f;
            jawTarget = Mathf.Sin(Time.time * 1.7f) * 6f;
            yield return null;
        }
    }

    /// Hop straight at the player, fast, until she's close (or the time runs out). No breathers.
    IEnumerator Pursue(float maxTime)
    {
        float end = Time.time + maxTime;
        while (Time.time < end && PlayerInRange && PlayerAlive())
        {
            Vector3 to = player.position - transform.position; to.y = 0f;
            float d = to.magnitude;
            if (d <= closeDistance) yield break;
            // aim a little ahead of a moving player
            Vector3 target = transform.position + to.normalized * Mathf.Min(chaseHopLength * (Enraged ? 1.15f : 1f), d - closeDistance * 0.6f);
            yield return Hop(target, hopHeight * 0.7f, T(chaseHopSeconds), facePlayer: true, quick: true);
        }
    }

    /// One hop from where she is to target (kept inside the arena).
    IEnumerator Hop(Vector3 target, float height, float time, bool facePlayer = false, bool bigBite = false, bool quick = false)
    {
        target = Clamp(target);
        Vector3 start = transform.position;
        // crouch (skipped for quick chase hops)
        if (!quick) yield return SquashTo(1f - squash, time * 0.15f);
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / time);
            Vector3 p = Vector3.Lerp(start, target, k); p.y = floorY;
            transform.position = p;
            lift = 4f * k * (1f - k) * height;
            squashY = 1f + squash * 0.8f * Mathf.Sin(k * Mathf.PI);
            Face(facePlayer && player ? player.position : target, 1f);
            jawTarget = bigBite ? Mathf.Lerp(JawOpen, JawShut, Mathf.Pow(k, 4f)) : Mathf.Sin(k * Mathf.PI) * JawOpen * 0.5f;
            yield return null;
        }
        transform.position = new Vector3(target.x, floorY, target.z);
        lift = 0f;
        if (quick) { squashY = 1f - squash; yield break; }   // bounces straight into the next hop
        yield return SquashTo(1f - squash, 0.06f);
        yield return SquashTo(1f, 0.1f);
    }

    IEnumerator SquashTo(float y, float time)
    {
        float from = squashY;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            squashY = Mathf.Lerp(from, y, t / time);
            yield return null;
        }
        squashY = y;
    }

    IEnumerator TurnToPlayer(float time)
    {
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            if (player) Face(player.position, 2f);
            yield return null;
        }
    }

    void Face(Vector3 point, float speedMul)
    {
        Vector3 d = point - transform.position; d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return;
        float want = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        yawDeg = Mathf.MoveTowardsAngle(yawDeg, want, turnSpeed * speedMul * (Enraged ? phase2Speed : 1f) * Time.deltaTime);
    }

    Vector3 Forward => Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;

    // ================================================================== attacks

    // Close range: jaw wide open for a moment (the warning), then a lunge that snaps shut on the player.
    IEnumerator Chomp()
    {
        if (!PlayerAlive()) yield break;
        float windup = T(chompWindup);
        for (float t = 0f; t < windup; t += Time.deltaTime)
        {
            Face(player.position, 3f);
            jawTarget = JawOpen * 1.4f;
            squashY = 1f + 0.12f * (t / windup) + Mathf.Sin(t * 50f) * 0.02f;
            yield return null;
        }
        Vector3 to = player.position - transform.position; to.y = 0f;
        Vector3 target = Clamp(transform.position + to.normalized * Mathf.Min(chompLunge, Mathf.Max(0.5f, to.magnitude - bodyRadius * 0.3f)));
        float time = T(0.28f);
        Vector3 start = transform.position;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            float k = t / time;
            Vector3 p = Vector3.Lerp(start, target, 1f - (1f - k) * (1f - k)); p.y = floorY;
            transform.position = p;
            lift = 4f * k * (1f - k) * 0.5f;
            jawTarget = Mathf.Lerp(JawOpen * 1.4f, JawShut, k * k);
            yield return null;
        }
        transform.position = target; lift = 0f;
        jawTarget = JawShut;
        if (player && FlatDistance(player.position, target) < bodyRadius + 1.1f && player.position.y < floorY + bodyHeight)
            BossDamage.HitPlayer(chompDamage, target, 2.5f);
        yield return SquashTo(0.85f, 0.06f);
        yield return SquashTo(1f, 0.12f);
        jawTarget = 0f;
        yield return new WaitForSeconds(T(0.15f));
    }

    // Three (or four) big hops at the player. Each landing hurts anyone close.
    IEnumerator BounceChase()
    {
        int hops = Enraged ? chaseHopsPhase2 : chaseHopsPhase1;
        for (int i = 0; i < hops; i++)
        {
            if (!PlayerAlive()) yield break;
            Vector3 to = player.position - transform.position; to.y = 0f;
            float reach = Mathf.Min(to.magnitude, 8f);
            Vector3 target = Clamp(transform.position + to.normalized * reach);
            float time = T(chaseHopTime);
            BossMarker.Disc(target, landRadius, warningColor, time + 0.05f, time * 0.5f);
            yield return Hop(target, chaseHopHeight, time, facePlayer: true, bigBite: true);
            Land(0.6f, 0f);
            HurtNear(target, landRadius, landDamage, 2f);
            BossShockwave.Make(target, landRadius + 1f, 9f, 0f, 0f, new Color(1f, 0.85f, 0.6f, 0.8f), floorY);
            yield return new WaitForSeconds(T(0.15f));
        }
        yield return new WaitForSeconds(T(0.2f));   // short opening for the player
    }

    // Curl up and roll at the player in a straight line. Rolling into the arena edge or a wall dazes her.
    IEnumerator Charge()
    {
        int charges = Enraged ? 2 : 1;
        for (int c = 0; c < charges; c++)
        {
            if (!PlayerAlive()) yield break;
            yield return TurnToPlayer(T(0.35f));
            Vector3 dir = player.position - transform.position; dir.y = 0f; dir.Normalize();
            yawDeg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float length = DistanceToEdge(transform.position, dir);
            BossMarker.Stripe(transform.position, dir, length, bodyRadius * 2.2f, warningColor, T(chargeWindup) + 0.2f);

            // wind-up: back up and wiggle
            Play(chargeSound);
            float windup = T(chargeWindup);
            Vector3 start = transform.position;
            for (float t = 0f; t < windup; t += Time.deltaTime)
            {
                float k = t / windup;
                transform.position = start - dir * Mathf.Sin(k * Mathf.PI * 0.5f) * 0.6f;
                squashY = 1f - 0.1f * k + Mathf.Sin(t * 40f) * 0.03f;
                spinDeg = -15f * k;
                jawTarget = JawShut;
                yield return null;
            }

            // roll
            bool hitPlayer = false, crashed = false;
            float speed = chargeSpeed * (Enraged ? phase2Speed : 1f);
            float travelled = 0f;
            squashY = 1f;
            while (true)
            {
                float step = speed * Time.deltaTime;
                Vector3 next = transform.position + dir * step;
                if (Blocked(transform.position, dir, step + bodyRadius) || FlatDistance(next, Center) > arenaRadius - bodyRadius)
                { crashed = true; break; }
                transform.position = next;
                travelled += step;
                spinDeg += step / Mathf.Max(0.3f, bodyRadius) * Mathf.Rad2Deg;
                if (!hitPlayer && player && FlatDistance(player.position, transform.position) < bodyRadius + 0.6f
                    && player.position.y < floorY + bodyHeight)
                    hitPlayer = BossDamage.HitPlayer(chargeDamage, transform.position, 3.5f);
                if (travelled > arenaRadius * 2.5f) break;
                yield return null;
            }

            // stop upright
            float from = Mathf.Repeat(spinDeg, 360f);
            for (float t = 0f; t < 0.2f; t += Time.deltaTime) { spinDeg = Mathf.Lerp(from, from > 180f ? 360f : 0f, t / 0.2f); yield return null; }
            spinDeg = 0f;

            if (crashed)
            {
                Land(0.8f, 0f);
                yield return Daze(dazeTime);
                yield break;
            }
            yield return new WaitForSeconds(T(0.25f));
        }
        yield return new WaitForSeconds(T(0.15f));
    }

    IEnumerator Daze(float time)
    {
        dazed = true;
        jawTarget = JawOpen * 0.6f;
        float baseYaw = yawDeg;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            yawDeg = baseYaw + Mathf.Sin(t * 6f) * 18f;          // dizzy sway
            squashY = 0.9f + Mathf.Sin(t * 9f) * 0.04f;
            yield return null;
        }
        squashY = 1f; jawTarget = 0f;
        dazed = false;
    }

    // Spit a fan of goo balls that leave hurting puddles. Phase 2: a wider fan, twice.
    IEnumerator GooSpit()
    {
        int volleys = Enraged ? 2 : 1;
        int count = Enraged ? spitPhase2 : spitPhase1;
        for (int v = 0; v < volleys; v++)
        {
            if (!PlayerAlive()) yield break;
            yield return TurnToPlayer(T(0.3f));
            // rear back with the jaw open
            jawTarget = JawOpen * 1.2f;
            yield return SquashTo(1.12f, T(0.5f));
            Play(spitSound);

            Vector3 toPlayer = player.position - transform.position; toPlayer.y = 0f;
            float dist = Mathf.Clamp(toPlayer.magnitude, 3f, arenaRadius * 1.6f);
            Vector3 mouth = transform.position + Vector3.up * bodyHeight * 0.4f + Forward * bodyRadius * 0.9f;
            float offset = v % 2 == 1 ? spitSpread / count * 0.5f : 0f;
            for (int i = 0; i < count; i++)
            {
                float a = count == 1 ? 0f : Mathf.Lerp(-spitSpread * 0.5f, spitSpread * 0.5f, i / (count - 1f)) + offset;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * toPlayer.normalized;
                Vector3 target = Clamp(transform.position + dir * dist * Random.Range(0.85f, 1.15f), 0.3f);
                target.y = floorY;
                float flight = T(spitFlightTime);
                BossMarker.Disc(target, puddleRadius, warningColor, flight, flight * 0.4f);
                BossGooBall.Throw(mouth, target, flight, 2.5f, 0.45f, gooColor, spitDamage, puddleRadius, puddleRadius, puddleTime, puddleDamage, floorY);
            }
            yield return SquashTo(0.88f, 0.08f);
            yield return SquashTo(1f, 0.2f);
            jawTarget = 0f;
            yield return new WaitForSeconds(T(0.3f));
        }
        yield return new WaitForSeconds(T(0.1f));
    }

    // Screech and call in some Nibblers.
    IEnumerator CallSwarm()
    {
        yield return TurnToPlayer(T(0.3f));
        Play(screechSound);
        jawTarget = JawOpen * 1.3f;
        for (float t = 0f; t < 1.0f; t += Time.deltaTime) { squashY = 1.1f + Mathf.Sin(t * 60f) * 0.04f; yield return null; }
        squashY = 1f; jawTarget = 0f;

        Vector2Int range = Enraged ? swarmPhase2 : swarmPhase1;
        int n = Mathf.Min(Random.Range(range.x, range.y + 1), maxSwarm - SwarmAlive());
        for (int i = 0; i < n; i++)
        {
            GameObject prefab = PickSwarmPrefab();
            if (!prefab) break;
            float a = (i / (float)Mathf.Max(1, n)) * 360f + Random.Range(-20f, 20f);
            Vector3 pos = Clamp(transform.position + Quaternion.Euler(0f, a, 0f) * Forward * (bodyRadius + 1.5f), 0.5f);
            pos.y = floorY;
            GameObject e = Instantiate(prefab, pos, Quaternion.LookRotation(pos - transform.position));
            PrepareMinion(e);
            swarm.Add(e);
            BossGooBall.Splat(pos, gooColor, 8, 0.5f);
            yield return new WaitForSeconds(0.15f);
        }
        yield return new WaitForSeconds(T(0.2f));
    }

    // Phase 2: leap high and land on the player, sending out a shockwave ring to jump over.
    IEnumerator BellyFlop()
    {
        if (!PlayerAlive()) yield break;
        yield return TurnToPlayer(T(0.25f));
        yield return SquashTo(0.7f, T(0.35f));                         // crouch
        Vector3 target = Clamp(player.position);
        target.y = floorY;
        float air = T(flopAirTime);
        BossMarker.Disc(target, flopRadius, warningColor, air + 0.05f, air * 0.8f);
        Vector3 start = transform.position;
        for (float t = 0f; t < air; t += Time.deltaTime)
        {
            float k = t / air;
            Vector3 p = Vector3.Lerp(start, target, k); p.y = floorY;
            transform.position = p;
            lift = 4f * k * (1f - k) * flopHeight;
            squashY = 1f + 0.2f * Mathf.Sin(k * Mathf.PI);
            jawTarget = JawOpen;
            Face(target, 2f);
            yield return null;
        }
        transform.position = target; lift = 0f;
        Land(1.2f, 0f);
        HurtNear(target, flopRadius, flopDamage, 3f);
        BossShockwave.Make(target, shockwaveRadius, shockwaveSpeed, shockwaveDamage, 2.5f, new Color(1f, 0.55f, 0.2f, 1f), floorY);
        yield return SquashTo(0.6f, 0.08f);
        yield return SquashTo(1f, 0.3f);
        jawTarget = 0f;
        yield return new WaitForSeconds(T(0.5f));                      // opening for the player
    }

    // Phase 2: goo drips from the ceiling around the player. Each spot is marked first.
    IEnumerator GooRain()
    {
        yield return TurnToPlayer(T(0.3f));
        Play(screechSound);
        jawTarget = JawOpen * 1.3f;
        yield return SquashTo(1.12f, 0.4f);
        float gap = rainTime / Mathf.Max(1, rainDrops);
        for (int i = 0; i < rainDrops; i++)
        {
            if (!PlayerAlive()) break;
            Vector3 near = player.position + new Vector3(Random.Range(-3.5f, 3.5f), 0f, Random.Range(-3.5f, 3.5f));
            if (i % 3 == 0) near = player.position;                       // every third one aims right at the player
            Vector3 target = Clamp(near, 0.3f); target.y = floorY;
            BossMarker.Disc(target, rainRadius, warningColor, rainWarning, rainWarning * 0.4f);
            BossGooBall.Throw(target + Vector3.up * 12f, target, rainWarning, 0f, 0.5f, gooColor, rainDamage, rainRadius,
                              rainRadius * 0.8f, puddleTime * 0.6f, puddleDamage, floorY);
            yield return new WaitForSeconds(gap);
        }
        yield return SquashTo(1f, 0.2f);
        jawTarget = 0f;
        yield return new WaitForSeconds(0.2f);
    }

    // ================================================================== defeat

    void OnDied()
    {
        if (Defeated) return;
        Defeated = true;
        FightActive = false;
        StopAllCoroutines();
        dazed = false;
        StartCoroutine(DefeatSequence());
    }

    IEnumerator DefeatSequence()
    {
        Play(defeatSound);
        BossHealthBar.Defeat(this);
        spinDeg = 0f; lift = 0f;

        // shake and swell, eyes flashing
        float t0 = Time.time;
        while (Time.time - t0 < 1.6f)
        {
            float k = (Time.time - t0) / 1.6f;
            squashY = 1f + 0.15f * k + Mathf.Sin(Time.time * 60f) * 0.05f;
            yawDeg += Mathf.Sin(Time.time * 45f) * 2f;
            jawTarget = JawOpen * 1.3f;
            yield return null;
        }

        // the crown pops off and stays as a trophy
        if (crown) StartCoroutine(PopCrown());

        // the big goo explosion
        Vector3 c = transform.position + Vector3.up * bodyHeight * 0.45f;
        BossGooBall.Splat(c, gooColor, explosionGoo, 1.6f);
        for (int i = 0; i < 14; i++)
        {
            Vector3 spot = Clamp(transform.position + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f)), 0.3f);
            spot.y = floorY;
            BossGooPuddle.Make(spot, Random.Range(0.6f, 1.4f), 6f, 0f, gooColor, floorY);
        }
        BossShockwave.Make(transform.position, 10f, 12f, 0f, 2f, new Color(gooColor.r, gooColor.g, gooColor.b, 1f), floorY);
        StartCoroutine(Flash(c));
        squashY = 1.3f;
        yield return SquashTo(0.2f, 0.25f);

        // her Nibblers burst too
        foreach (GameObject e in swarm)
        {
            if (!e) continue;
            EnemyStats s = e.GetComponent<EnemyStats>();
            if (s && !s.IsDead) s.Die(); else if (!s) Destroy(e);
        }
        swarm.Clear();

        SetWalls(false);
        yield return new WaitForSeconds(0.6f);

        // the reward drops from above
        if (lootCratePrefab) StartCoroutine(DropCrate());
        QuestManager.TriggerEvent(questEventId);
        onDefeated.Invoke();
        // DeathRespawn hides her with a puff of goo smoke a moment later
    }

    IEnumerator PopCrown()
    {
        crown.SetParent(null, true);
        Vector3 v = (Vector3.up * 7f) + Random.insideUnitSphere * 2f;
        Vector3 spinAxis = Random.onUnitSphere;
        float bounces = 0;
        while (bounces < 3)
        {
            v += Physics.gravity * Time.deltaTime;
            Vector3 p = crown.position + v * Time.deltaTime;
            if (p.y < floorY) { p.y = floorY; v = new Vector3(v.x * 0.6f, -v.y * 0.4f, v.z * 0.6f); bounces++; }
            crown.position = p;
            crown.Rotate(spinAxis, 400f * Time.deltaTime * (3 - bounces) / 3f, Space.World);
            yield return null;
        }
        // settle tipped over on its side
        Quaternion from = crown.rotation, to = Quaternion.Euler(0f, crown.eulerAngles.y, 70f);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { crown.rotation = Quaternion.Slerp(from, to, t / 0.4f); yield return null; }
    }

    IEnumerator DropCrate()
    {
        Vector3 spot = crateSpawnPoint ? crateSpawnPoint.position : new Vector3(transform.position.x, floorY, transform.position.z);
        BossMarker.Disc(spot, 1.2f, new Color(1f, 0.85f, 0.3f, 0.9f), 1.2f, 0.3f);
        GameObject crate = Instantiate(lootCratePrefab, spot + Vector3.up * 12f, Quaternion.Euler(0f, yawDeg + 180f, 0f));
        float v = 0f;
        while (crate && crate.transform.position.y > spot.y)
        {
            v += 30f * Time.deltaTime;
            crate.transform.position += Vector3.down * v * Time.deltaTime;
            yield return null;
        }
        if (!crate) yield break;
        crate.transform.position = spot;
        BossShockwave.Make(spot, 3f, 8f, 0f, 0f, new Color(1f, 0.85f, 0.4f, 0.9f), floorY);
        // little bounce
        Vector3 full = crate.transform.localScale;
        for (float t = 0f; t < 0.35f && crate; t += Time.deltaTime)
        {
            float k = t / 0.35f;
            float s = Mathf.Sin(k * Mathf.PI) * 0.18f * (1f - k);
            crate.transform.localScale = Vector3.Scale(full, new Vector3(1f + s, 1f - s, 1f + s));
            crate.transform.position = spot + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.4f * (k > 0.3f ? 1f : 0f);
            yield return null;
        }
        if (crate) { crate.transform.localScale = full; crate.transform.position = spot; }
    }

    IEnumerator Flash(Vector3 at)
    {
        var go = new GameObject("QueenDefeatFlash");
        go.transform.position = at;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = Color.Lerp(gooColor, Color.white, 0.4f); l.range = 14f; l.shadows = LightShadows.None;
        for (float t = 0f; t < 0.8f; t += Time.deltaTime) { l.intensity = Mathf.Lerp(10f, 0f, t / 0.8f); yield return null; }
        Destroy(go);
    }

    // ================================================================== player death / reset

    void OnPlayerDied()
    {
        if (!FightActive || Defeated || !resetWhenPlayerDies) return;
        StartCoroutine(ResetAfter(1.2f));
    }

    IEnumerator ResetAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        ResetFight();
    }

    /// Puts her back at the start with full health. The fight starts again when the player returns.
    public void ResetFight()
    {
        if (Defeated) return;
        if (fight != null) StopCoroutine(fight);
        StopAllCoroutines();
        BossHazard.ClearAll();
        foreach (GameObject e in swarm) if (e) Destroy(e);
        swarm.Clear();

        FightActive = false; Enraged = false; dazed = false;
        stats.SetHealth(maxHealth); lastHealth = maxHealth;
        transform.position = home; yawDeg = homeRot.eulerAngles.y; spinDeg = 0f; lift = 0f; squashY = 1f; jawTarget = 0f;
        ClearEyes();
        SetWalls(false);
        BossHealthBar.Hide(this);
        if (dropInIntro) SetHidden(true);
    }

    void ClearEyes()
    {
        foreach (var pair in EyeSlots())
        {
            eyeBlock.Clear();
            pair.r.SetPropertyBlock(eyeBlock, pair.i);
        }
    }

    // ================================================================== helpers

    bool PlayerAlive()
    {
        return player && (PlayerStats.Instance == null || !PlayerStats.Instance.IsDead);
    }

    void HurtNear(Vector3 at, float radius, float damage, float knock)
    {
        if (!player) return;
        if (FlatDistance(player.position, at) < radius && player.position.y < floorY + 1.2f)
            BossDamage.HitPlayer(damage, at, knock);
    }

    void Land(float strength, float damage)
    {
        Play(landSound);
        BossGooBall.Splat(new Vector3(transform.position.x, floorY, transform.position.z), new Color(0.55f, 0.45f, 0.4f), Mathf.RoundToInt(8 * strength), 0.4f);
    }

    Vector3 Clamp(Vector3 p, float extraMargin = 0f)
    {
        Vector3 c = Center;
        Vector3 d = p - c; d.y = 0f;
        float max = Mathf.Max(0.5f, arenaRadius - bodyRadius - extraMargin);
        if (d.magnitude > max) d = d.normalized * max;
        return new Vector3(c.x + d.x, p.y, c.z + d.z);
    }

    float DistanceToEdge(Vector3 from, Vector3 dir)
    {
        float d = 0f;
        while (d < arenaRadius * 2.5f && FlatDistance(from + dir * d, Center) < arenaRadius - bodyRadius && !Blocked(from + dir * d, dir, 0.5f))
            d += 0.5f;
        return Mathf.Max(1f, d);
    }

    // a wall or rock in the way (ignores the player, enemies, triggers and herself)
    bool Blocked(Vector3 from, Vector3 dir, float distance)
    {
        Vector3 origin = new Vector3(from.x, floorY + bodyHeight * 0.4f, from.z);
        foreach (RaycastHit h in Physics.SphereCastAll(origin, bodyRadius * 0.6f, dir, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(transform)) continue;
            if (player && t.IsChildOf(player)) continue;
            if (h.collider.GetComponentInParent<EnemyHitReaction>() || h.collider.GetComponentInParent<AlienWildWest.PestChomper>()) continue;
            if (h.collider.GetComponentInParent<CharacterController>()) continue;
            if (h.point.y < floorY + 0.25f && h.normal.y > 0.6f) continue;   // the floor itself
            return true;
        }
        return false;
    }

    static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    bool CanSwarm() => swarmPrefabs.Exists(p => p != null) && SwarmAlive() < maxSwarm;

    int SwarmAlive()
    {
        swarm.RemoveAll(e => e == null);
        int n = 0;
        foreach (GameObject e in swarm)
        {
            if (!e.activeInHierarchy) continue;
            EnemyStats s = e.GetComponent<EnemyStats>();
            if (s && s.IsDead) continue;
            n++;
        }
        return n;
    }

    GameObject PickSwarmPrefab()
    {
        var valid = swarmPrefabs.FindAll(p => p != null);
        return valid.Count > 0 ? valid[Random.Range(0, valid.Count)] : null;
    }

    // Same setup the spawners use: they don't respawn, and they get an aim ring.
    static void PrepareMinion(GameObject e)
    {
        EnemyStats s = e.GetComponent<EnemyStats>();
        if (s == null && e.GetComponent<AlienWildWest.PestChomper>() != null) s = e.AddComponent<EnemyStats>();
        if (s != null) s.ShouldRespawn = false;
        if (e.GetComponent<AimTarget>() == null) e.AddComponent<AimTarget>();
    }

    void SetWalls(bool on)
    {
        if (lockWalls == null) return;
        foreach (GameObject w in lockWalls) if (w) w.SetActive(on);
    }

    void SetHidden(bool hide)
    {
        hidden = hide;
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = !hide;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = !hide;
        if (aimTarget) aimTarget.enabled = !hide;
    }

    void Play(AudioClip clip) { if (clip && audioSource) audioSource.PlayOneShot(clip); }

    void OnDrawGizmosSelected()
    {
        Vector3 c = arenaCenter ? arenaCenter.position : transform.position;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);
        DrawCircle(c, arenaRadius);
        Gizmos.color = new Color(0.3f, 1f, 0.3f, 1f);
        DrawCircle(c, startRange);
        Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.6f);
        DrawCircle(c, leaveRange);
    }

    static void DrawCircle(Vector3 c, float r)
    {
        Vector3 prev = c + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= 48; i++)
        {
            float a = i / 48f * Mathf.PI * 2f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, p); prev = p;
        }
    }
}