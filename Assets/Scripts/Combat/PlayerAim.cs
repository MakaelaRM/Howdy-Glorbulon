using UnityEngine;
using UnityEngine.InputSystem;

/// Mouse aiming for a low camera, with depth assist and a green aim arrow under the player's feet.
/// Put this on the player, on the same object as the CharacterController.
/// - The player aims (and faces) toward the mouse cursor on the ground. A gamepad's right stick also works.
/// - Snap: if the cursor is on or near an enemy on screen, that enemy is the target.
/// - Depth assist: otherwise, enemies roughly in the aim direction are picked. The assist is tight when
///   aiming left/right on screen (easy to judge) and wide when aiming toward/away from the camera
///   (hard to judge). Shots go to the target's body height, so bouncing Nibblers get hit.
/// Enemies need the AimTarget component to be aimed at.
[DefaultExecutionOrder(-50)]
public class PlayerAim : MonoBehaviour
{
    public static PlayerAim Instance { get; private set; }

    [Header("Assist")]
    public float assistRange = 12f;
    [Tooltip("If the cursor is this close to an enemy on screen (as a fraction of screen height), aim straight at it.")]
    [Range(0f, 0.2f)] public float cursorSnap = 0.07f;
    [Tooltip("Degrees off the aim line a target may be when aiming left/right on screen (easy to judge, so small).")]
    public float lateralAssistAngle = 12f;
    [Tooltip("Degrees off the aim line a target may be when aiming toward/away from the camera (hard to judge, so wide).")]
    public float depthAssistAngle = 35f;
    [Tooltip("Prefer the current target a little so it doesn't flicker between two enemies.")]
    [Range(0f, 1f)] public float stickiness = 0.25f;

    [Header("Aim Marker (small chevron >)")]
    public bool showArrow = true;
    public Color arrowColor = new Color(0.3f, 0.85f, 0.3f, 1f);
    [Tooltip("Slightly brighter colour while it's pointing at a target.")]
    public Color arrowTargetColor = new Color(0.55f, 1f, 0.45f, 1f);
    [Tooltip("Distance from the player's centre to the chevron, in metres.")]
    public float arrowStart = 0.5f;
    [Tooltip("Size of the chevron (half its width), in metres.")]
    public float chevronSize = 0.1f;
    [Tooltip("Line thickness of the chevron, in metres.")]
    public float chevronThickness = 0.025f;
    [Tooltip("Optional. Leave empty for a plain unlit material. If the arrow shows pink, assign an Unlit material here.")]
    public Material arrowMaterial;

    [Header("Facing")]
    [Tooltip("After shooting or swinging, keep facing the attack direction this long (seconds) before turning back to the walking direction.")]
    public float faceAfterAttackTime = 0.45f;

    /// Flat aim direction (no height), always set.
    public Vector3 AimDirection { get; private set; } = Vector3.forward;
    /// The enemy the next shot will go to, or null.
    public AimTarget CurrentTarget { get; private set; }
    /// Which way the player should face.
    public Vector3 FacingDirection => CurrentTarget ? Flat(CurrentTarget.AimPoint - transform.position, AimDirection) : AimDirection;
    /// True for a moment after an attack (FaceNow).
    public bool HoldFacing => Time.time < faceHoldUntil;

    CharacterController controller;
    Transform arrow;
    Material arrowMat;
    float faceHoldUntil;

    void Awake()
    {
        Instance = this;
        controller = GetComponent<CharacterController>();
        AimDirection = Flat(transform.forward, Vector3.forward);
    }

    void Start() { if (showArrow) BuildArrow(); }

    void OnDisable()
    {
        if (CurrentTarget) CurrentTarget.SetTargeted(false);
        if (arrow) arrow.gameObject.SetActive(false);
        if (Instance == this) Instance = null;
    }

    void OnEnable() { if (arrow) arrow.gameObject.SetActive(true); }
    void OnDestroy() { if (arrow) Destroy(arrow.gameObject); }

    /// Direction to fire from the muzzle: straight at the target if there is one, otherwise along the aim.
    public Vector3 GetShotDirection(Vector3 muzzle)
    {
        if (CurrentTarget) return (CurrentTarget.AimPoint - muzzle).normalized;
        return AimDirection;
    }

    /// Instantly turn the player to face a direction.
    public void FaceNow(Vector3 dir)
    {
        dir = Flat(dir, transform.forward);
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        faceHoldUntil = Time.time + faceAfterAttackTime;
    }

    /// Turn to face the current target if it is within maxDistance. Returns true if it turned.
    public bool FaceCurrentTarget(float maxDistance)
    {
        if (!CurrentTarget) return false;
        Vector3 to = CurrentTarget.AimPoint - transform.position; to.y = 0f;
        if (to.magnitude > maxDistance) return false;
        FaceNow(to);
        return true;
    }

    Vector3 Feet => controller ? transform.position + controller.center - Vector3.up * (controller.height * 0.5f) : transform.position;

    void Update()
    {
        Camera cam = Camera.main;
        if (!cam) return;
        Vector3 camFwd = Flat(cam.transform.forward, Vector3.forward);
        Vector3 camRight = Vector3.Cross(Vector3.up, camFwd);

        // ---- aim direction: gamepad right stick, otherwise the mouse
        var pad = Gamepad.current;
        Vector2 stick = pad != null ? pad.rightStick.ReadValue() : Vector2.zero;
        if (stick.sqrMagnitude > 0.04f)
        {
            AimDirection = (camRight * stick.x + camFwd * stick.y).normalized;
        }
        else if (Mouse.current != null)
        {
            Vector2 mouse = Mouse.current.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(mouse);
            Plane ground = new Plane(Vector3.up, Feet);
            Vector3 dir = Vector3.zero;
            if (ground.Raycast(ray, out float d) && d < 200f)
                dir = Flat(ray.GetPoint(d) - transform.position, Vector3.zero);
            if (dir == Vector3.zero)
            {
                // cursor above the horizon: use its direction on screen instead
                Vector3 me = cam.WorldToScreenPoint(transform.position);
                Vector2 delta = mouse - new Vector2(me.x, me.y);
                if (delta.sqrMagnitude > 4f) dir = (camRight * delta.x + camFwd * delta.y).normalized;
            }
            if (dir != Vector3.zero) AimDirection = dir;
        }

        PickTarget(cam, camFwd);
    }

    void LateUpdate() { UpdateArrow(); }

    void PickTarget(Camera cam, Vector3 camFwd)
    {
        AimTarget best = null; float bestScore = float.MaxValue;

        // 1. enemy right under / near the cursor
        if (Mouse.current != null && cursorSnap > 0f)
        {
            Vector2 mouse = Mouse.current.position.ReadValue();
            float snapPx = cursorSnap * Screen.height;
            foreach (AimTarget t in AimTarget.All)
            {
                if (!t || !t.CanBeTargeted) continue;
                Vector3 to = t.AimPoint - transform.position; to.y = 0f;
                if (to.magnitude > assistRange) continue;
                Vector3 sp = cam.WorldToScreenPoint(t.AimPoint);
                if (sp.z <= 0f) continue;
                float px = Vector2.Distance(mouse, new Vector2(sp.x, sp.y));
                if (t == CurrentTarget) px *= 0.8f;
                if (px < snapPx && px < bestScore) { bestScore = px; best = t; }
            }
        }

        // 2. otherwise, depth assist along the aim direction
        if (best == null)
        {
            foreach (AimTarget t in AimTarget.All)
            {
                if (!t || !t.CanBeTargeted) continue;
                Vector3 to = t.AimPoint - transform.position; to.y = 0f;
                float dist = to.magnitude;
                if (dist > assistRange || dist < 0.01f) continue;
                float angle = Vector3.Angle(AimDirection, to);
                float depthness = Mathf.Abs(Vector3.Dot(AimDirection, camFwd));
                float allowed = Mathf.Lerp(lateralAssistAngle, depthAssistAngle, depthness);
                if (t == CurrentTarget) allowed *= 1.25f;
                if (angle > allowed) continue;
                float score = angle / allowed + 0.6f * dist / assistRange;
                if (t == CurrentTarget) score -= stickiness;
                if (score < bestScore) { bestScore = score; best = t; }
            }
        }

        if (best != CurrentTarget)
        {
            if (CurrentTarget) CurrentTarget.SetTargeted(false);
            CurrentTarget = best;
            if (CurrentTarget) CurrentTarget.SetTargeted(true);
        }
    }

    // ---------------------------------------------------------------- arrow
    void BuildArrow()
    {
        var go = new GameObject("AimArrow");   // not a child, so it doesn't tilt or squash with the player
        arrow = go.transform;
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // small flat chevron ">" lying on the ground, pointing along +Z
        float z0 = arrowStart, c = chevronSize, t = chevronThickness;
        Vector3 tip = new Vector3(0f, 0f, z0 + c * 0.9f);
        Vector3[] ends = { new Vector3(-c, 0f, z0), new Vector3(c, 0f, z0) };
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        foreach (Vector3 e in ends)
        {
            Vector3 along = (tip - e).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, along) * (t * 0.5f);
            int k = verts.Count;
            verts.Add(e - side); verts.Add(e + side); verts.Add(tip + side + along * t * 0.5f); verts.Add(tip - side + along * t * 0.5f);
            // both windings so it shows whichever way the quad faces
            tris.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3, k, k + 2, k + 1, k, k + 3, k + 2 });
        }
        var mesh = new Mesh { name = "AimChevron" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        var normals = new Vector3[verts.Count];
        for (int n = 0; n < normals.Length; n++) normals[n] = Vector3.up;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        if (arrowMaterial) arrowMat = new Material(arrowMaterial);
        else
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) sh = Shader.Find("Sprites/Default");
            if (sh) arrowMat = new Material(sh);
        }
        if (arrowMat) { arrowMat.color = arrowColor; mr.sharedMaterial = arrowMat; }
    }

    void UpdateArrow()
    {
        if (!arrow) return;
        Vector3 dir = FacingDirection;
        arrow.SetPositionAndRotation(Feet + Vector3.up * 0.04f, Quaternion.LookRotation(dir, Vector3.up));
        if (arrowMat) arrowMat.color = CurrentTarget ? arrowTargetColor : arrowColor;
    }

    static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : fallback;
    }
}