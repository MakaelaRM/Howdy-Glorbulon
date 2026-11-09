using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Death animation shared by the player and enemies: tip over, vanish in a puff of smoke,
/// then reappear at the respawn point. PlayerStats and EnemyStats add this automatically.
public class DeathRespawn : MonoBehaviour
{
    [Header("Respawn")]
    [Tooltip("Where to reappear. Leave empty to use where this object started in the scene.")]
    public Transform respawnPoint;
    [Tooltip("Seconds spent gone before reappearing.")]
    public float respawnDelay = 2f;

    [Header("Fall over")]
    [Tooltip("Seconds to tip over.")]
    public float fallTime = 0.45f;
    [Tooltip("Degrees to tip over. 90 = flat on its back.")]
    public float fallAngle = 90f;
    [Tooltip("Seconds lying on the ground before turning to smoke.")]
    public float lieTime = 0.5f;

    [Header("Smoke")]
    public Color smokeColor = new Color(0.6f, 0.6f, 0.63f, 0.9f);
    [Tooltip("Size of the smoke puff. 1 suits something about the player's size.")]
    public float smokeSize = 1f;
    [Tooltip("Also puff smoke when reappearing.")]
    public bool smokeOnRespawn = true;

    [Header("Paused while dead")]
    [Tooltip("Scripts switched off while dead. If left empty, every other script on this object is used.")]
    public List<Behaviour> pauseWhileDead = new List<Behaviour>();
    [Tooltip("Scripts left on until the smoke, so the killing hit's red flash still plays. If left empty, the hit reaction script is used.")]
    public List<Behaviour> pauseWhenGone = new List<Behaviour>();

    public bool IsPlaying { get; private set; }

    Vector3 spawnPosition;
    Quaternion spawnRotation;
    Vector3 spawnScale;
    readonly List<Behaviour> extraPaused = new List<Behaviour>();
    readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    readonly List<Collider> hiddenColliders = new List<Collider>();

    static Material smokeMaterial;

    void Start()
    {
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        spawnScale = transform.localScale;

        if (pauseWhenGone.Count == 0)
        {
            foreach (MonoBehaviour b in GetComponents<MonoBehaviour>())
                if (b is PlayerHitReaction || b is EnemyHitReaction) pauseWhenGone.Add(b);
        }

        if (pauseWhileDead.Count == 0)
        {
            foreach (MonoBehaviour b in GetComponents<MonoBehaviour>())
                if (b != this && !(b is PlayerStats) && !(b is EnemyStats) && !pauseWhenGone.Contains(b)) pauseWhileDead.Add(b);
        }
    }

    // For scripts on other objects that should also stop, like CharacterMovement.
    public void AlsoPause(Behaviour b)
    {
        if (b != null && !extraPaused.Contains(b)) extraPaused.Add(b);
    }

    public void Play(Action onRespawn, bool respawn = true)
    {
        if (IsPlaying) return;
        StartCoroutine(Run(onRespawn, respawn));
    }

    IEnumerator Run(Action onRespawn, bool respawn)
    {
        IsPlaying = true;
        SetPaused(true);

        Bounds bounds = RendererBounds();
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        Vector3 pivot = new Vector3(startPos.x, bounds.min.y, startPos.z);
        Vector3 axis = transform.right;

        for (float t = 0f; t < fallTime; t += Time.deltaTime)
        {
            float k = t / fallTime;
            ApplyFall(pivot, axis, startPos, startRot, k * k);
            yield return null;
        }
        ApplyFall(pivot, axis, startPos, startRot, 1f);

        yield return new WaitForSeconds(lieTime);

        SpawnSmoke(RendererBounds().center, smokeSize, smokeColor);
        SetHidden(true);
        SetEnabled(pauseWhenGone, false);

        // Permanent death: stay hidden and switched off.
        if (!respawn)
        {
            IsPlaying = false;
            yield break;
        }

        yield return new WaitForSeconds(respawnDelay);

        Vector3 pos = respawnPoint != null ? respawnPoint.position : spawnPosition;
        Quaternion rot = respawnPoint != null ? respawnPoint.rotation : spawnRotation;
        Teleport(pos, rot);
        transform.localScale = spawnScale;

        SetHidden(false);
        if (smokeOnRespawn) SpawnSmoke(RendererBounds().center, smokeSize * 0.8f, smokeColor);

        SetPaused(false);
        SetEnabled(pauseWhenGone, true);
        IsPlaying = false;
        onRespawn?.Invoke();
    }

    void ApplyFall(Vector3 pivot, Vector3 axis, Vector3 startPos, Quaternion startRot, float amount)
    {
        Quaternion tip = Quaternion.AngleAxis(-fallAngle * amount, axis);
        transform.SetPositionAndRotation(pivot + tip * (startPos - pivot), tip * startRot);
    }

    void Teleport(Vector3 pos, Quaternion rot)
    {
        var cc = GetComponent<CharacterController>();
        bool ccWasOn = cc != null && cc.enabled;
        if (ccWasOn) cc.enabled = false;

        transform.SetPositionAndRotation(pos, rot);

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = pos;
            rb.rotation = rot;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        if (ccWasOn) cc.enabled = true;
    }

    void SetPaused(bool paused)
    {
        SetEnabled(pauseWhileDead, !paused);
        SetEnabled(extraPaused, !paused);
    }

    static void SetEnabled(List<Behaviour> list, bool on)
    {
        foreach (Behaviour b in list)
            if (b != null) b.enabled = on;
    }

    void SetHidden(bool hidden)
    {
        if (hidden)
        {
            hiddenRenderers.Clear();
            hiddenColliders.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hiddenRenderers.Add(r);
            }
            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c is CharacterController) continue;
                c.enabled = false;
                hiddenColliders.Add(c);
            }
        }
        else
        {
            foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
            foreach (Collider c in hiddenColliders) if (c != null) c.enabled = true;
            hiddenRenderers.Clear();
            hiddenColliders.Clear();
        }
    }

    Bounds RendererBounds()
    {
        bool any = false;
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (!any)
            {
                b = r.bounds;
                any = true;
            }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    public static void SpawnSmoke(Vector3 position, float size, Color color)
    {
        var go = new GameObject("DeathSmoke");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f * size, 1.5f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f * size, 0.8f * size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.35f * size;

        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.3f * size;
        limit.dampen = 0.12f;

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = SmokeMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
        UnityEngine.Object.Destroy(go, 3f);
    }

    static Material SmokeMaterial()
    {
        if (smokeMaterial != null) return smokeMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        smokeMaterial = new Material(shader);

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        smokeMaterial.mainTexture = tex;
        return smokeMaterial;
    }
}
