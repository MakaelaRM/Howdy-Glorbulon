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

    private int reputationPoints; 
    private int totalReputation;  
    private float health;
    private float shield;

    public event Action Changed;

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

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("There's more than one PlayerStats in the scene. Using the newest one.", this);
        Instance = this;
        health = maxHealth;
        shield = maxShield;
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
        health = value;
        Changed?.Invoke();
    }

    public void SetShield(float value)
    {
        value = Mathf.Clamp(value, 0f, maxShield);
        if (Mathf.Approximately(value, shield)) return;
        shield = value;
        Changed?.Invoke();
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