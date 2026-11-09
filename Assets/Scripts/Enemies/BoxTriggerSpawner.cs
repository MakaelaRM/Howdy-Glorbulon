using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Spawns an object when another object enters this BoxCollider.
/// Attach to a GameObject; a BoxCollider (set as trigger) is added automatically.
/// Note: at least one of the two objects (the one entering or this one) needs a Rigidbody
/// for Unity to fire trigger events.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class BoxTriggerSpawner : MonoBehaviour
{
    public enum SpawnLocation
    {
        SpawnPoint,     // At the Spawn Point transform (or this object if none is set)
        BoxCenter,      // At the center of the box
        EnteringObject  // Where the entering object touched the box
    }

    [Header("What to spawn")]
    [Tooltip("Prefabs to choose from. One is picked at random for each object spawned.")]
    public List<GameObject> objectsToSpawn = new List<GameObject>();

    [Tooltip("Optional parent for spawned objects.")]
    public Transform spawnParent;

    [Header("Where to spawn")]
    public SpawnLocation spawnLocation = SpawnLocation.SpawnPoint;

    [Tooltip("Used when Spawn Location is SpawnPoint. Falls back to this object's position if empty.")]
    public Transform spawnPoint;

    [Tooltip("Offset applied to the spawn position (world space).")]
    public Vector3 offset = Vector3.zero;

    [Header("Who triggers it")]
    [Tooltip("Only objects with this tag trigger a spawn. Leave empty to allow anything.")]
    public string requiredTag = "Player";

    [Tooltip("Only objects on these layers trigger a spawn.")]
    public LayerMask triggerLayers = ~0;

    [Header("Behavior")]
    [Tooltip("Number of objects to spawn per trigger.")]
    [Min(1)] public int count = 1;

    [Tooltip("Only allow the first trigger, then never again.")]
    public bool spawnOnce = false;

    [Tooltip("Seconds to wait before another spawn is allowed. 0 = no cooldown.")]
    [Min(0f)] public float cooldown = 0f;

    [Tooltip("Destroy the object that entered after it triggers a spawn.")]
    public bool destroyEnteringObject = false;

    [Tooltip("Match the rotation of the spawn point instead of the prefab's own rotation.")]
    public bool useSpawnPointRotation = false;

    [Tooltip("Whether spawned enemies come back after dying.")]
    public bool shouldRespawn = true;

    [Header("Events")]
    public UnityEvent<GameObject> onSpawned;

    private BoxCollider box;
    private bool hasSpawned;
    private float nextAllowedTime;

    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        if (!box.isTrigger)
            Debug.LogWarning($"{name}: BoxCollider is not set to 'Is Trigger'. Enabling it.", this);
        box.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!PassesFilter(other.gameObject)) return;
        if (spawnOnce && hasSpawned) return;
        if (Time.time < nextAllowedTime) return;

        if (!HasValidPrefab())
        {
            Debug.LogWarning($"{name}: objectsToSpawn is empty (or only has empty slots).", this);
            return;
        }

        Vector3 position = GetSpawnPosition(other) + offset;
        Transform respawnMarker = shouldRespawn ? CreateRespawnMarker(position) : null;

        for (int i = 0; i < count; i++)
        {
            // Each spawned object gets its own random pick from the list.
            GameObject prefab = PickRandomPrefab();

            Quaternion rotation = (useSpawnPointRotation && spawnPoint != null)
                ? spawnPoint.rotation
                : prefab.transform.rotation;

            GameObject spawned = Instantiate(prefab, position, rotation, spawnParent);
            ApplyRespawnSetting(spawned, respawnMarker);
            onSpawned?.Invoke(spawned);
        }

        hasSpawned = true;
        nextAllowedTime = Time.time + cooldown;

        if (destroyEnteringObject)
            Destroy(other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject);
    }

    private Transform CreateRespawnMarker(Vector3 position)
        {
            var marker = new GameObject($"{name}_RespawnPoint").transform;
            marker.SetParent(transform, true);
            marker.position = position;
            marker.rotation = (useSpawnPointRotation && spawnPoint != null)
                ? spawnPoint.rotation
                : Quaternion.identity;
            return marker;
        }

    private void ApplyRespawnSetting(GameObject spawned, Transform respawnMarker)
        {
            EnemyStats stats = spawned.GetComponent<EnemyStats>();

            // Enemies without an EnemyStats normally get one on their first hit.
            // Add it now so the toggle takes effect.
            if (stats == null && spawned.GetComponent<AlienWildWest.PestChomper>() != null)
                stats = spawned.AddComponent<EnemyStats>();

            if (stats == null) return;

            stats.ShouldRespawn = shouldRespawn;

            DeathRespawn death = spawned.GetComponent<DeathRespawn>();
            if (death != null && respawnMarker != null)
                death.respawnPoint = respawnMarker;
        }
    private bool HasValidPrefab()
    {
        if (objectsToSpawn == null) return false;
        foreach (var go in objectsToSpawn)
            if (go != null) return true;
        return false;
    }

    // Picks uniformly at random from the non-empty entries in the list.
    private GameObject PickRandomPrefab()
    {
        var valid = new List<GameObject>(objectsToSpawn.Count);
        foreach (var go in objectsToSpawn)
            if (go != null) valid.Add(go);

        return valid[Random.Range(0, valid.Count)];
    }

    private bool PassesFilter(GameObject go)
    {
        if ((triggerLayers.value & (1 << go.layer)) == 0) return false;
        if (!string.IsNullOrEmpty(requiredTag) && !go.CompareTag(requiredTag)) return false;
        return true;
    }

    private Vector3 GetSpawnPosition(Collider other)
    {
        switch (spawnLocation)
        {
            case SpawnLocation.BoxCenter:
                return transform.TransformPoint(box.center);

            case SpawnLocation.EnteringObject:
                return other.ClosestPoint(transform.TransformPoint(box.center));

            default:
                return spawnPoint != null ? spawnPoint.position : transform.position;
        }
    }

    private void OnDrawGizmos()
    {
        var bc = GetComponent<BoxCollider>();
        if (bc == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.3f);
        Gizmos.DrawCube(bc.center, bc.size);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);
        Gizmos.DrawWireCube(bc.center, bc.size);

        if (spawnLocation == SpawnLocation.SpawnPoint && spawnPoint != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(spawnPoint.position + offset, 0.25f);
        }
    }
} 