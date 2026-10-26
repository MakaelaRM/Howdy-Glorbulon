using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(150)]
[RequireComponent(typeof(WeaponSwap))]
public class BlasterAttack : MonoBehaviour
{
    [Header("Shooting")]
    [Tooltip("Seconds between shots.")]
    public float fireDelay = 0.25f;
    [Tooltip("Bullet speed in metres per second.")]
    public float bulletSpeed = 25f;
    [Tooltip("A bullet that hits nothing is removed after this many metres.")]
    public float maxDistance = 80f;
    [Tooltip("Thickness of the bullet for hit checks, in metres.")]
    public float bulletRadius = 0.08f;
    public LayerMask hitLayers = ~0;
    [Tooltip("On: bullets fly level instead of following any up/down tilt of the gun.")]
    public bool keepLevel = true;
    [Tooltip("Enemies within this many degrees of straight ahead pull the shot down to their height, since they're shorter than the gun. 0 = off.")]
    public float aimAssistAngle = 15f;
    [Tooltip("How far away aim assist looks for enemies, in metres.")]
    public float aimAssistRange = 25f;

    [Header("Bullet look")]
    [Tooltip("Drag Bullet.obj (or any model) here. Its long side should run along its Z axis.")]
    public GameObject bulletModel;
    public float bulletScale = 0.35f;
    public Color bulletColor = new Color(0.35f, 1f, 0.3f);
    [Tooltip("Leave empty to use a plain glowing material in Bullet Color.")]
    public Material bulletMaterial;
    public bool bulletLight = true;
    public bool bulletTrail = true;

    [Header("Recoil")]
    [Tooltip("How far the arm kicks up per shot, in degrees.")]
    public float recoilAngle = 12f;
    [Tooltip("How far the gun jolts back per shot, in metres.")]
    public float recoilKickBack = 0.06f;
    public float recoilTime = 0.15f;

    [Header("Sound (optional)")]
    public AudioClip shootSound;
    public AudioClip hitSound;

    WeaponSwap weapons;
    CharacterMovement movement;
    AudioSource audioSource;
    float fireTimer;
    float recoilTimer = -1f;
    bool shotQueued;
    bool warnedNoModel;
    Transform muzzleGun;
    Transform muzzleTip;
    Vector3 muzzleLocal;
    Material solidMat;
    Material fxMat;

    void Awake()
    {
        weapons = GetComponent<WeaponSwap>();
        audioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        movement = FindAnyObjectByType<CharacterMovement>();
    }

    void Update()
    {
        if (fireTimer > 0f) fireTimer -= Time.deltaTime;

        if (fireTimer <= 0f && ClickedThisFrame() && CanShoot())
        {
            shotQueued = true;
            fireTimer = fireDelay;
        }
    }

    bool ClickedThisFrame()
    {
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    bool CanShoot()
    {
        if (!weapons.GunOut || weapons.IsSwapping) return false;
        if (Time.timeScale <= 0f) return false;
        if (movement != null && movement.inventoryUI != null && movement.inventoryUI.activeInHierarchy) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
        return true;
    }

    void LateUpdate()
    {
        if (shotQueued)
        {
            shotQueued = false;
            if (weapons.GunOut && !weapons.IsSwapping && Fire()) recoilTimer = 0f;
        }

        if (recoilTimer < 0f) return;

        recoilTimer += Time.deltaTime;
        float k = 1f - recoilTimer / Mathf.Max(0.01f, recoilTime);
        if (k <= 0f || !weapons.GunOut || weapons.IsSwapping)
        {
            recoilTimer = -1f;
            return;
        }
        ApplyRecoil(k * k);
    }

    bool Fire()
    {
        Transform gun = weapons.gun != null ? weapons.gun.weapon : null;
        if (gun == null) return false;

        Vector3 muzzlePos = MuzzlePosition(gun);
        Vector3 dir = gun.forward;
        if (keepLevel) dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
        dir.Normalize();
        dir = AimAssist(muzzlePos, dir);

        Vector3 fromBody = muzzlePos - transform.position;
        float behind = Mathf.Max(0f, Vector3.Dot(fromBody, dir));
        Vector3 castStart = muzzlePos - dir * behind;

        SpawnBullet(muzzlePos, dir, castStart);
        BlasterImpact.Spawn(muzzlePos, dir, bulletColor, SolidMaterial(), FxMaterial(), 0.5f, false);

        if (shootSound != null && audioSource != null) audioSource.PlayOneShot(shootSound);
        return true;
    }

    Vector3 MuzzlePosition(Transform gun)
    {
        if (gun != muzzleGun)
        {
            muzzleGun = gun;
            muzzleTip = null;
            foreach (Transform t in gun.GetComponentsInChildren<Transform>())
            {
                if (t != gun && t.name.ToLower().EndsWith("_tip"))
                {
                    muzzleTip = t;
                    break;
                }
            }
            if (muzzleTip == null) muzzleLocal = FindFront(gun);
        }
        return muzzleTip != null ? muzzleTip.position : gun.TransformPoint(muzzleLocal);
    }

    Vector3 FindFront(Transform gun)
    {
        bool any = false;
        Bounds local = new Bounds();
        foreach (MeshFilter mf in gun.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Bounds b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = gun.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any)
                {
                    local = new Bounds(p, Vector3.zero);
                    any = true;
                }
                else local.Encapsulate(p);
            }
        }
        return any ? new Vector3(local.center.x, local.center.y, local.max.z) : Vector3.zero;
    }

    Vector3 AimAssist(Vector3 muzzlePos, Vector3 dir)
    {
        if (aimAssistAngle <= 0f) return dir;

        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        if (flat.sqrMagnitude < 1e-4f) return dir;
        flat.Normalize();

        float bestAngle = aimAssistAngle;
        EnemyHitReaction best = null;
        foreach (Collider c in Physics.OverlapSphere(transform.position, Mathf.Min(aimAssistRange, maxDistance), hitLayers, QueryTriggerInteraction.Collide))
        {
            if (c.transform.IsChildOf(transform)) continue;
            EnemyHitReaction enemy = EnemyHitReaction.FindOn(c);
            if (enemy == null || enemy == best) continue;

            Vector3 to = enemy.Center - muzzlePos;
            to.y = 0f;
            if (to.sqrMagnitude < 0.04f) continue;
            float angle = Vector3.Angle(flat, to);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = enemy;
            }
        }
        if (best == null) return dir;

        Vector3 target = best.Center;
        Vector3 flatTo = target - muzzlePos;
        flatTo.y = 0f;
        float ahead = Mathf.Max(0.2f, Vector3.Dot(flatTo, flat));
        return (flat * ahead + Vector3.up * (target.y - muzzlePos.y)).normalized;
    }

    void SpawnBullet(Vector3 position, Vector3 dir, Vector3 castStart)
    {
        GameObject go;
        if (bulletModel != null)
        {
            go = Instantiate(bulletModel);
            go.transform.localScale = bulletModel.transform.localScale * bulletScale;
        }
        else
        {
            if (!warnedNoModel)
            {
                Debug.LogWarning("[BlasterAttack] No Bullet Model set, using a capsule. Drag Bullet.obj into Bullet Model.", this);
                warnedNoModel = true;
            }
            go = FallbackBullet();
            go.transform.localScale = Vector3.one * bulletScale;
        }
        go.name = "BlasterBullet";

        foreach (Collider c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);

        Material mat = bulletMaterial != null ? bulletMaterial : SolidMaterial();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(dir));

        if (bulletLight)
        {
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = bulletColor;
            light.range = 2.5f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
        }

        Material fx = FxMaterial();
        if (bulletTrail && fx != null)
        {
            TrailRenderer trail = go.AddComponent<TrailRenderer>();
            trail.material = fx;
            trail.time = 0.08f;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = 0.08f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(bulletColor, 0f), new GradientColorKey(bulletColor, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
        }

        BlasterBullet bullet = go.AddComponent<BlasterBullet>();
        bullet.owner = transform;
        bullet.direction = dir;
        bullet.speed = bulletSpeed;
        bullet.maxDistance = maxDistance;
        bullet.radius = bulletRadius;
        bullet.hitLayers = hitLayers;
        bullet.impactColor = bulletColor;
        bullet.impactMaterial = SolidMaterial();
        bullet.sparkMaterial = fx;
        bullet.hitSound = hitSound;
        bullet.Launch(castStart);
    }

    GameObject FallbackBullet()
    {
        var root = new GameObject();
        GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        DestroyImmediate(capsule.GetComponent<Collider>());
        capsule.transform.SetParent(root.transform, false);
        capsule.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        capsule.transform.localScale = new Vector3(0.2f, 0.45f, 0.2f);
        return root;
    }

    void ApplyRecoil(float k)
    {
        Transform arm = weapons.rightArm;
        Transform gun = weapons.gun != null ? weapons.gun.weapon : null;
        Quaternion kick = Quaternion.AngleAxis(-recoilAngle * k, transform.right);

        if (arm != null) arm.rotation = kick * arm.rotation;

        if (gun != null)
        {
            Vector3 pivot = arm != null ? arm.position : gun.position;
            gun.position = pivot + kick * (gun.position - pivot) - transform.forward * (recoilKickBack * k);
            gun.rotation = kick * gun.rotation;
        }
    }

    Material SolidMaterial()
    {
        if (solidMat == null) solidMat = MakeMaterial("Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default");
        if (solidMat != null) solidMat.color = bulletColor;
        return solidMat;
    }

    Material FxMaterial()
    {
        if (fxMat == null) fxMat = MakeMaterial("Sprites/Default", "Universal Render Pipeline/Particles/Unlit");
        return fxMat;
    }

    static Material MakeMaterial(params string[] shaderNames)
    {
        foreach (string s in shaderNames)
        {
            Shader shader = Shader.Find(s);
            if (shader != null) return new Material(shader);
        }
        return null;
    }
}
