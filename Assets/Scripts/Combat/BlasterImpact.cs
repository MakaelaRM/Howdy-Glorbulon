using UnityEngine;

public class BlasterImpact : MonoBehaviour
{
    public float flashTime = 0.18f;

    Transform flash;
    Light flashLight;
    float lightIntensity;
    float size = 1f;
    float timer;

    public static void Spawn(Vector3 position, Vector3 normal, Color color, Material flashMaterial, Material sparkMaterial, float size, bool sparks)
    {
        var go = new GameObject("BlasterImpact");
        Quaternion rot = normal.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        go.transform.SetPositionAndRotation(position, rot);

        BlasterImpact fx = go.AddComponent<BlasterImpact>();
        fx.size = size;

        if (flashMaterial != null)
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(ball.GetComponent<Collider>());
            ball.transform.SetParent(go.transform, false);
            ball.transform.localScale = Vector3.zero;
            Renderer r = ball.GetComponent<Renderer>();
            r.sharedMaterial = flashMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            fx.flash = ball.transform;
        }

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 3f * size;
        light.intensity = 4f;
        light.shadows = LightShadows.None;
        fx.flashLight = light;
        fx.lightIntensity = light.intensity;

        if (sparks && sparkMaterial != null) AddSparks(go, color, sparkMaterial, size);

        Destroy(go, 1f);
    }

    static void AddSparks(GameObject go, Color color, Material material, float size)
    {
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * size, 4f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f * size, 0.07f * size);
        main.startColor = color;
        main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 32;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)14) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 60f;
        shape.radius = 0.02f;

        ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        ParticleSystem.SizeOverLifetimeModule shrink = ps.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.04f;
        renderer.lengthScale = 1f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
    }

    void Update()
    {
        timer += Time.deltaTime;
        float t = timer / Mathf.Max(0.01f, flashTime);

        if (flash != null)
        {
            float peak = 0.35f * size;
            float s = t < 0.3f ? Mathf.Lerp(0f, peak, t / 0.3f) : Mathf.Lerp(peak, 0f, (t - 0.3f) / 0.7f);
            flash.localScale = Vector3.one * Mathf.Max(0f, s);
            if (t >= 1f)
            {
                Destroy(flash.gameObject);
                flash = null;
            }
        }

        if (flashLight != null) flashLight.intensity = lightIntensity * Mathf.Clamp01(1f - t);
    }
}
