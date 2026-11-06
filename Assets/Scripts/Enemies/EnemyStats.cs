using System;
using UnityEngine;

/// Health, shield and contact damage for an enemy.
/// Enemies without one get one automatically (with these defaults) the first time they're hit or touch the player.
public class EnemyStats : MonoBehaviour
{
    [Header("Health & Shield")]
    [SerializeField, Min(1f)] private float maxHealth = 50f;
    [SerializeField, Min(0f)] private float maxShield = 0f;
    [Tooltip("Shield regained per second once it starts recharging.")]
    [SerializeField, Min(0f)] private float shieldRecharge = 5f;
    [Tooltip("Seconds without taking damage before the shield starts recharging.")]
    [SerializeField, Min(0f)] private float shieldRechargeDelay = 3f;

    [Header("Attack")]
    [Tooltip("Damage dealt to the player on contact.")]
    [SerializeField, Min(0f)] private float damage = 30f;

    private float health;
    private float shield;
    private float sinceDamage;
    private DeathRespawn death;

    public event Action Died;
    public event Action Respawned;

    public float Health => health;
    public float MaxHealth => maxHealth;
    public float Shield => shield;
    public float MaxShield => maxShield;
    public float Damage => damage;
    public bool IsDead { get; private set; }

    public static EnemyStats On(EnemyHitReaction enemy)
    {
        if (enemy == null) return null;
        EnemyStats stats = enemy.GetComponent<EnemyStats>();
        if (stats == null) stats = enemy.gameObject.AddComponent<EnemyStats>();
        return stats;
    }

    public static bool IsDeadOn(EnemyHitReaction enemy)
    {
        if (enemy == null) return false;
        EnemyStats stats = enemy.GetComponent<EnemyStats>();
        return stats != null && stats.IsDead;
    }

    void Awake()
    {
        health = maxHealth;
        shield = maxShield;
        death = GetComponent<DeathRespawn>();
        if (death == null) death = gameObject.AddComponent<DeathRespawn>();
    }

    void Update()
    {
        if (IsDead) return;

        sinceDamage += Time.deltaTime;
        if (maxShield > 0f && shield < maxShield && sinceDamage >= shieldRechargeDelay)
            shield = Mathf.Min(maxShield, shield + shieldRecharge * Time.deltaTime);
    }

    public void SetHealth(float value)
    {
        value = Mathf.Clamp(value, 0f, maxHealth);
        if (value < health) sinceDamage = 0f;
        health = value;
    }

    public void SetShield(float value)
    {
        value = Mathf.Clamp(value, 0f, maxShield);
        if (value < shield) sinceDamage = 0f;
        shield = value;
    }

    public void Die()
    {
        if (IsDead) return;
        IsDead = true;
        health = 0f;
        Died?.Invoke();
        death.Play(Respawn);
    }

    private void Respawn()
    {
        health = maxHealth;
        shield = maxShield;
        sinceDamage = 0f;
        IsDead = false;
        Respawned?.Invoke();
    }
}
