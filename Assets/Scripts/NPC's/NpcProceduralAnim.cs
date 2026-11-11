using UnityEngine;

// Gives an NPC built from separate parts (Head, Body, Leg_L, Leg_R, Arm_L, Arm_R) an idle and a walk animation
// without any animation clips. Add it to the same object as NpcWalker (or NpcRoutine). It measures how fast the
// object is moving, so it needs no connection to those scripts.
public class NpcProceduralAnim : MonoBehaviour
{
    [Header("Parts (found automatically by name ending: _Head _Body _Arm_L _Arm_R _Leg_L _Leg_R)")]
    public Transform head;
    public Transform body;
    public Transform legL, legR, armL, armR;

    [Header("Idle (standing still)")]
    public float idleBobHeight = 0.03f;
    public float idleBobSpeed = 2f;
    [Tooltip("How far the head looks left and right, in degrees.")]
    public float idleLookAngle = 10f;

    [Header("Walk")]
    public bool swingLimbs = true;
    public float walkSwingAngle = 35f;
    [Tooltip("Steps per second.")]
    public float stepRate = 2.2f;
    public float walkBobHeight = 0.05f;
    [Tooltip("Axis the legs and arms swing around, in the part's own space. Try (0,0,1) if they swing sideways.")]
    public Vector3 swingAxis = new Vector3(1f, 0f, 0f);
    public float moveThreshold = 0.05f;

    [Header("Set by WeaponSwap")]
    [Tooltip("On: the right arm stops swinging and holds still (used when a weapon is out).")]
    public bool armRHeld;
    public float armRHeldAngle;

    Vector3 lastPos;
    float moveAmount;
    float walkPhase;
    float idleTime;
    Transform[] bobParts;
    Vector3[] bobStart;
    Quaternion headStartRot;
    Quaternion legLStart, legRStart, armLStart, armRStart;

    // Finds a part by the end of its name, e.g. "_Leg_L" matches "Char_Native_Prospector_Leg_L".
    // Looks under this object first, then under the whole NPC (the root of this hierarchy).
    Transform Find(string suffix)
    {
        suffix = suffix.ToLower();
        var r = Search(transform, suffix);
        if (r == null && transform.root != transform) r = Search(transform.root, suffix);
        return r;
    }

    Transform Search(Transform top, string suffix)
    {
        foreach (var t in top.GetComponentsInChildren<Transform>(true))
        {
            if (t == top) continue;
            if (InsideWeapon(t, top)) continue; // don't grab a held weapon's own _Body etc.
            if (t.name.ToLower().EndsWith(suffix)) return t;
        }
        return null;
    }

    static bool InsideWeapon(Transform t, Transform top)
    {
        for (var p = t; p != null && p != top; p = p.parent)
            if (p.name.StartsWith("Wpn_", System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    void Warn(string what)
    {
        Debug.LogWarning("[NpcProceduralAnim] " + name + ": could not find the " + what +
                         ". Drag it into the slot, or check the name ends with the right suffix.", this);
    }

    // Runs when you add the component, and from the component's right-click menu > Find Parts.
    [ContextMenu("Find Parts")]
    void FindParts()
    {
        if (head == null) { head = Find("_Head"); if (head == null) Warn("head (name ending _Head)"); }
        if (body == null) { body = Find("_Body"); if (body == null) Warn("body (name ending _Body)"); }
        if (legL == null) { legL = Find("_Leg_L"); if (legL == null) Warn("left leg (name ending _Leg_L)"); }
        if (legR == null) { legR = Find("_Leg_R"); if (legR == null) Warn("right leg (name ending _Leg_R)"); }
        if (armL == null) { armL = Find("_Arm_L"); if (armL == null) Warn("left arm (name ending _Arm_L)"); }
        if (armR == null) { armR = Find("_Arm_R"); if (armR == null) Warn("right arm (name ending _Arm_R)"); }
    }

    void Reset() { FindParts(); }

    void Start()
    {
        FindParts();

        var list = new System.Collections.Generic.List<Transform>();
        foreach (var t in new[] { head, body, armL, armR }) if (t != null && !list.Contains(t)) list.Add(t);
        bobParts = list.ToArray();
        bobStart = new Vector3[bobParts.Length];
        for (int i = 0; i < bobParts.Length; i++) bobStart[i] = bobParts[i].localPosition;

        if (head != null) headStartRot = head.localRotation;
        if (legL != null) legLStart = legL.localRotation;
        if (legR != null) legRStart = legR.localRotation;
        if (armL != null) armLStart = armL.localRotation;
        if (armR != null) armRStart = armR.localRotation;

        lastPos = transform.position;
        idleTime = Random.Range(0f, 10f); // so NPCs don't breathe in sync
    }

    public void ResetPose()
    {
        if (bobParts == null) return;

        for (int i = 0; i < bobParts.Length; i++)
            if (bobParts[i] != null) bobParts[i].localPosition = bobStart[i];

        if (head != null) head.localRotation = headStartRot;
        if (legL != null) legL.localRotation = legLStart;
        if (legR != null) legR.localRotation = legRStart;
        if (armL != null) armL.localRotation = armLStart;
        if (armR != null)
            armR.localRotation = armRHeld ? armRStart * Quaternion.AngleAxis(armRHeldAngle, swingAxis.normalized) : armRStart;

        moveAmount = 0f;
        walkPhase = 0f;
        lastPos = transform.position;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - lastPos;
        delta.y = 0f;
        float speed = delta.magnitude / dt;
        lastPos = transform.position;

        moveAmount = Mathf.MoveTowards(moveAmount, speed > moveThreshold ? 1f : 0f, dt * 6f);
        walkPhase += dt * stepRate * Mathf.PI * 2f * moveAmount;
        idleTime += dt;

        float bob = Mathf.Sin(idleTime * idleBobSpeed) * idleBobHeight * (1f - moveAmount)
                  + Mathf.Abs(Mathf.Sin(walkPhase)) * walkBobHeight * moveAmount;
        for (int i = 0; i < bobParts.Length; i++)
            if (bobParts[i] != null) bobParts[i].localPosition = bobStart[i] + Vector3.up * bob;

        if (head != null)
        {
            float look = Mathf.Sin(idleTime * 0.7f) * idleLookAngle * (1f - moveAmount);
            head.localRotation = headStartRot * Quaternion.Euler(0f, look, 0f);
        }

        if (swingLimbs)
        {
            float s = Mathf.Sin(walkPhase) * walkSwingAngle * moveAmount;
            Vector3 axis = swingAxis.normalized;
            if (legL != null) legL.localRotation = legLStart * Quaternion.AngleAxis(s, axis);
            if (legR != null) legR.localRotation = legRStart * Quaternion.AngleAxis(-s, axis);
            if (armL != null) armL.localRotation = armLStart * Quaternion.AngleAxis(-s * 0.8f, axis);
            if (armR != null && !armRHeld) armR.localRotation = armRStart * Quaternion.AngleAxis(s * 0.8f, axis);
        }

        if (armRHeld && armR != null)
            armR.localRotation = armRStart * Quaternion.AngleAxis(armRHeldAngle, swingAxis.normalized);
    }
}