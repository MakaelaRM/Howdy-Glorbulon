// Alien Wild West - bounce-and-chomp behaviour for the Nibbler pests.
// Put it on the root of a placed Enemy_Pest_Nibbler_* model. It finds the "_Jaw" child by itself.
// The pest turns to face its target, hops toward it with squash-and-stretch, and chomps:
// a lazy chomp while far away, and a big wide bite that snaps shut on landing when it is close.
// Pests keep their distance from each other (Personal Space) and each one heads for its own spot around
// the target, so a swarm surrounds the player instead of piling into one blob.
// This moves the transform directly (colliders alone don't stop that). If your enemies use a Rigidbody or
// NavMeshAgent, leave "Move Toward Target" off and just use this for the bounce and jaw animation.
using System.Collections.Generic;
using UnityEngine;

namespace AlienWildWest
{
    public class PestChomper : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Who to chase. If empty, the object tagged \"Player\" is used.")]
        public Transform target;
        public bool moveTowardTarget = true;
        [Tooltip("How far from the target each pest stops, on its own spot around the target (metres).")]
        public float stopDistance = 0.7f;
        [Tooltip("Pests push away from each other inside this distance (metres). About the pest's width or a bit more.")]
        public float personalSpace = 0.6f;
        [Tooltip("How hard they push apart.")]
        public float separationStrength = 3f;

        [Header("Hopping")]
        public float hopHeight = 0.35f;
        [Tooltip("Seconds per hop.")]
        public float hopTime = 0.55f;
        [Tooltip("Metres covered per hop.")]
        public float hopDistance = 0.7f;
        [Tooltip("How much it squashes on landing and stretches in the air (0 = none).")]
        public float squash = 0.22f;
        public float turnSpeed = 540f;

        [Header("Chomping")]
        [Tooltip("Within this distance every hop is a big bite.")]
        public float biteRange = 1.6f;
        [Tooltip("Jaw angle for a wide-open bite, in degrees from the imported pose.")]
        public float openAngle = 22f;
        [Tooltip("Jaw angle when snapped shut, in degrees from the imported pose (the model is exported slightly open).")]
        public float closedAngle = -10f;
        [Tooltip("Tick this if the jaw opens upward instead of down.")]
        public bool invertJaw = false;
        [Tooltip("Offset in seconds so a swarm doesn't hop in sync. Leave at 0 for a random offset.")]
        public float phase = 0f;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent onBite;

        static readonly List<PestChomper> All = new List<PestChomper>();
        static int nextSlot;
        float slotAngle;
        Transform jaw;
        Quaternion jawRest;
        Vector3 baseScale;
        float groundY;
        float t;
        float stunTimer;

        public bool IsStunned => stunTimer > 0f;

        public void Stun(float seconds)
        {
            stunTimer = Mathf.Max(stunTimer, seconds);
        }

        void Start()
        {
            foreach (Transform c in GetComponentsInChildren<Transform>())
                if (c != transform && c.name.EndsWith("_Jaw")) { jaw = c; break; }
            if (jaw) jawRest = jaw.localRotation;
            else Debug.LogWarning("PestChomper: no child ending in _Jaw found under " + name);
            baseScale = transform.localScale;
            groundY = transform.position.y;
            if (!target)
            {
                GameObject p = GameObject.FindWithTag("Player");
                if (p) target = p.transform;
            }
            if (phase == 0f) phase = Random.Range(0f, hopTime);
            t = phase;
            slotAngle = (nextSlot++) * 137.5f;            // golden-angle spacing: each pest gets its own side
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void StunnedPose()
        {
            float hop = Mathf.Max(0.1f, hopTime);
            t = Mathf.Floor(t / hop) * hop;

            Vector3 pos = transform.position;
            pos.y = Mathf.MoveTowards(pos.y, groundY, 4f * Time.deltaTime);
            transform.position = pos;

            float shake = 1f + 0.06f * Mathf.Sin(Time.time * 50f);
            Vector3 wobble = Vector3.Scale(baseScale, new Vector3(shake, 1f / shake, shake));
            transform.localScale = Vector3.Lerp(transform.localScale, wobble, 20f * Time.deltaTime);

            if (jaw)
            {
                float ang = openAngle * 0.7f;
                if (invertJaw) ang = -ang;
                jaw.localRotation = Quaternion.Slerp(jaw.localRotation, jawRest * Quaternion.Euler(ang, 0f, 0f), 12f * Time.deltaTime);
            }
        }

        void Update()
        {
            if (stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                StunnedPose();
                return;
            }

            float prev = t;
            t += Time.deltaTime;
            float hop = Mathf.Max(0.1f, hopTime);
            float u = Mathf.Repeat(t, hop) / hop;                 // 0..1 through the current hop
            bool landed = Mathf.Repeat(prev, hop) / hop > u;      // wrapped round, so we just landed

            float dist = Mathf.Infinity;
            Vector3 moveDir = Vector3.zero; float goalDist = 0f;
            if (target)
            {
                Vector3 to = target.position - transform.position; to.y = 0f;
                dist = to.magnitude;
                if (dist > 1e-3f)
                {
                    Quaternion want = Quaternion.LookRotation(to / dist, Vector3.up);   // always face the player
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
                }
                // head for this pest's own spot on a ring around the target
                Vector3 goal = target.position + Quaternion.Euler(0f, slotAngle, 0f) * Vector3.forward * stopDistance;
                Vector3 toGoal = goal - transform.position; toGoal.y = 0f;
                goalDist = toGoal.magnitude;
                if (goalDist > 1e-3f) moveDir = toGoal / goalDist;
            }

            // keep apart from the other pests, on the ground and in the air
            Vector3 push = Vector3.zero;
            foreach (PestChomper o in All)
            {
                if (o == this) continue;
                Vector3 away = transform.position - o.transform.position; away.y = 0f;
                float d = away.magnitude;
                if (d >= personalSpace) continue;
                if (d < 1e-3f) away = Quaternion.Euler(0f, slotAngle, 0f) * Vector3.right; else away /= d;
                push += away * (1f - d / personalSpace);
            }
            if (target)
            {   // and don't hop inside the player either
                Vector3 away = transform.position - target.position; away.y = 0f;
                float d = away.magnitude, min = stopDistance * 0.8f;
                if (d < min && d > 1e-3f) push += away / d * (1f - d / min) * 2f;
            }
            transform.position += push * separationStrength * Time.deltaTime;

            // hop arc: in the air for most of the cycle, a short squash on the ground
            const float air = 0.8f;
            float y = 0f, sy = 1f;
            if (u < air)
            {
                float a = u / air;
                y = 4f * a * (1f - a) * hopHeight;
                sy = 1f + squash * 0.6f * Mathf.Sin(a * Mathf.PI);            // stretch in the air
                if (moveTowardTarget && target && goalDist > 0.05f)
                    transform.position += moveDir * Mathf.Min(hopDistance / (hop * air) * Time.deltaTime, goalDist);
            }
            else
            {
                float g = (u - air) / (1f - air);
                sy = 1f - squash * Mathf.Sin(g * Mathf.PI);                    // squash on landing
            }
            Vector3 pos = transform.position; pos.y = groundY + y; transform.position = pos;
            float sx = 1f / Mathf.Sqrt(Mathf.Max(0.2f, sy));                   // keep the volume roughly the same
            transform.localScale = Vector3.Scale(baseScale, new Vector3(sx, sy, sx));

            // jaw: wide open on the way in, snapping shut right at landing when close; lazy chomps otherwise
            if (jaw)
            {
                float ang;
                bool biting = dist < biteRange;
                if (biting)
                {
                    if (u < 0.55f) ang = Mathf.Lerp(0f, openAngle, Mathf.SmoothStep(0f, 1f, u / 0.55f));
                    else if (u < 0.8f) ang = Mathf.Lerp(openAngle, closedAngle, Mathf.Pow((u - 0.55f) / 0.25f, 3f));   // snap
                    else ang = Mathf.Lerp(closedAngle, 0f, (u - 0.8f) / 0.2f);
                    if (landed) onBite.Invoke();
                }
                else
                {
                    ang = Mathf.Lerp(closedAngle * 0.5f, openAngle * 0.4f, 0.5f + 0.5f * Mathf.Sin(u * 2f * Mathf.PI));
                }
                if (invertJaw) ang = -ang;
                jaw.localRotation = jawRest * Quaternion.Euler(ang, 0f, 0f);
            }
        }
    }
}