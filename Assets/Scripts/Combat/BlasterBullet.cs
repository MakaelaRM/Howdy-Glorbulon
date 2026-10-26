using UnityEngine;

public class BlasterBullet : MonoBehaviour
{
    public Transform owner;
    public Vector3 direction = Vector3.forward;
    public float speed = 25f;
    public float maxDistance = 80f;
    public float radius = 0.08f;
    public LayerMask hitLayers = ~0;
    public Color impactColor = Color.green;
    public Material impactMaterial;
    public Material sparkMaterial;
    public AudioClip hitSound;

    float travelled;
    bool launched;
    bool done;

    public void Launch(Vector3 castFrom)
    {
        launched = true;
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : transform.forward;
        Sweep(castFrom, transform.position);
    }

    void Update()
    {
        if (!launched || done) return;

        float step = speed * Time.deltaTime;
        Vector3 from = transform.position;
        Vector3 to = from + direction * step;
        if (Sweep(from, to)) return;

        transform.position = to;
        travelled += step;
        if (travelled >= maxDistance)
        {
            done = true;
            Destroy(gameObject);
        }
    }

    bool Sweep(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 1e-5f) return false;
        Vector3 dir = d / len;

        bool found = false;
        RaycastHit best = default;
        foreach (RaycastHit h in Physics.SphereCastAll(from, radius, dir, len, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (h.collider == null) continue;
            if (owner != null && h.collider.transform.IsChildOf(owner)) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (!found || h.distance < best.distance)
            {
                best = h;
                found = true;
            }
        }
        if (!found) return false;

        Vector3 point = best.point;
        Vector3 normal = best.normal;
        if (best.distance <= 0f && point == Vector3.zero)
        {
            point = from;
            normal = -dir;
        }
        Hit(best.collider, point, normal);
        return true;
    }

    void Hit(Collider c, Vector3 point, Vector3 normal)
    {
        done = true;

        EnemyHitReaction enemy = EnemyHitReaction.FindOn(c);
        if (enemy != null) enemy.TakeHit(point - direction);

        BlasterImpact.Spawn(point, normal, impactColor, impactMaterial, sparkMaterial, 1f, true);
        if (hitSound != null) AudioSource.PlayClipAtPoint(hitSound, point);

        Destroy(gameObject);
    }
}
