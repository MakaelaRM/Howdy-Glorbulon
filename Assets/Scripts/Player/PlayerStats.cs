using System;
using UnityEngine;

/// Put one on the Player. Other scripts reach it through PlayerStats.Instance.
public class PlayerStats : MonoBehaviour
{
    public static PlayerStats Instance { get; private set; }

    [Header("Starting Values")]
    [SerializeField, Min(0)] private int money = 0;
    [SerializeField, Min(0)] private int food = 0;   
    [SerializeField, Min(0)] private int drinks = 0;
    [SerializeField, Min(0)] private int reputationLevel = 0;    

    [Header("Reputation")]
    [Tooltip("Reputation points needed to go from level 0 to level 1.")]
    [SerializeField, Min(1)] private int reputationPerLevel = 100;
    [Tooltip("Extra points needed for each level after that. 0 = every level costs the same.")]
    [SerializeField, Min(0)] private int extraPerLevel = 25;

    [Header("Health & Shield")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [SerializeField, Min(0f)] private float maxShield = 50f;
    [Tooltip("Shield regained per second once it starts recharging.")]
    [SerializeField, Min(0f)] private float shieldRecharge = 5f;
    [Tooltip("Seconds without taking damage before the shield starts recharging.")]
    [SerializeField, Min(0f)] private float shieldRechargeDelay = 3f;

    [Header("Carry Over")]
    [Tooltip("Moving between these scenes keeps health, shield and blaster heat. Entering one from any other scene refills them.")]
    [SerializeField] private string[] combatScenes = { "Mine", "MineLevel1", "MineLevel2" };

    private int reputationPoints; 
    private int totalReputation;  
    private static float health;
    private static float shield;
    private static float sinceDamage;
    private static string lastScene;
    private DeathRespawn death;

    public event Action Changed;
    public event Action Died;
    public event Action Respawned;

    public int Money => money;
    public int Food => food;
    public int Drinks => drinks;
    public int ReputationLevel => reputationLevel;
    public int ReputationPoints => reputationPoints;
    public int PointsToNextLevel => reputationPerLevel + extraPerLevel * reputationLevel;
    public int TotalReputation => totalReputation;
    public float ReputationProgress => (float)reputationPoints / PointsToNextLevel;
    public float Health => health;
    public float MaxHealth => maxHealth;
    public float Shield => shield;
    public float MaxShield => maxShield;
    public float HealthPercent => health / maxHealth;
    public float ShieldPercent => maxShield > 0f ? shield / maxShield : 0f;
    public bool IsDead { get; private set; }
    public static bool CarriedOver { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        health = 0f;
        shield = 0f;
        sinceDamage = 0f;
        lastScene = null;
        CarriedOver = false;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("There's more than one PlayerStats in the scene. Using the newest one.", this);
        Instance = this;

        string scene = gameObject.scene.name;
        CarriedOver = IsCombatScene(lastScene) && IsCombatScene(scene) && health > 0f;
        lastScene = scene;

        if (CarriedOver)
        {
            health = Mathf.Min(health, maxHealth);
            shield = Mathf.Min(shield, maxShield);
        }
        else
        {
            health = maxHealth;
            shield = maxShield;
            sinceDamage = 0f;
        }

        death = GetComponent<DeathRespawn>();
        if (death == null) death = gameObject.AddComponent<DeathRespawn>();
    }

    void Start()
    {
        foreach (CharacterMovement movement in FindObjectsByType<CharacterMovement>())
            if (movement.controller != null && movement.controller.gameObject == gameObject) death.AlsoPause(movement);
    }

    void Update()
    {
        if (IsDead) return;

        sinceDamage += Time.deltaTime;
        if (maxShield > 0f && shield < maxShield && sinceDamage >= shieldRechargeDelay)
        {
            shield = Mathf.Min(maxShield, shield + shieldRecharge * Time.deltaTime);
            Changed?.Invoke();
        }
    }

    private bool IsCombatScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || combatScenes == null) return false;
        foreach (string s in combatScenes)
            if (string.Equals(s, sceneName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void AddMoney(int amount)  => Change(ref money, amount);
    public void AddFood(int amount)   => Change(ref food, amount);
    public void AddDrinks(int amount) => Change(ref drinks, amount);

    public void SetHealth(float value)
    {
        value = Mathf.Clamp(value, 0f, maxHealth);
        if (Mathf.Approximately(value, health)) return;
        if (value < health) sinceDamage = 0f;
        health = value;
        Changed?.Invoke();
    }


    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        SetHealth(health + amount);
    }




    public void SetShield(float value)
    {
        value = Mathf.Clamp(value, 0f, maxShield);
        if (Mathf.Approximately(value, shield)) return;
        if (value < shield) sinceDamage = 0f;
        shield = value;
        Changed?.Invoke();
    }

    public void TakeDamage(EnemyStats enemy)
    {
        if (enemy == null || IsDead) return;

        float damage = enemy.Damage;
        if (damage <= 0f) return;

        if (shield > 0f)
        {
            if (damage <= shield) SetShield(shield - damage);
            else SetShield(0f);
        }
        else if (damage < health)
        {
            SetHealth(health - damage);
        }
        else
        {
            Die();
        }
    }

    public void Die()
    {
        if (IsDead) return;
        IsDead = true;
        health = 0f;
        sinceDamage = 0f;
        Changed?.Invoke();
        Died?.Invoke();
        death.Play(Respawn);
    }

    private void Respawn()
    {
        health = maxHealth;
        shield = maxShield;
        sinceDamage = 0f;
        IsDead = false;
        Changed?.Invoke();
        Respawned?.Invoke();
    }

    public void AddReputation(int points)
    {
        if (points == 0) return;

        reputationPoints += points;
        totalReputation += points;

        while (reputationPoints >= PointsToNextLevel)
        {
            reputationPoints -= PointsToNextLevel;
            reputationLevel++;
        }

        if (reputationPoints < 0)
        {
            totalReputation -= reputationPoints;
            reputationPoints = 0;
        }

        Changed?.Invoke();
    }

    private void Change(ref int stat, int amount)
    {
        if (amount == 0) return;
        stat = Mathf.Max(0, stat + amount);
        Changed?.Invoke();
    }



}