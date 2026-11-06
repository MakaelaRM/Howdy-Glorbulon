using System;
using UnityEngine;

/// Put this on the Player Alien next to BlasterAttack.
/// Every shot adds heat. Heat drains when you stop firing, even while the melee weapon is out.
/// At full heat the gun overheats: he lifts it barrel-up to vent, it drains slower, and it can't fire until it's cooled.
[DefaultExecutionOrder(160)]
[RequireComponent(typeof(WeaponSwap))]
public class BlasterHeat : MonoBehaviour
{
    [Header("Heat")]
    [Tooltip("Heat added per shot. The gun overheats at 1.")]
    [Range(0.01f, 1f)] public float heatPerShot = 0.15f;
    [Tooltip("Heat lost per second while not firing.")]
    public float coolRate = 0.35f;
    [Tooltip("Seconds after a shot before the gun starts cooling.")]
    public float coolDelay = 0.35f;

    [Header("Overheat")]
    [Tooltip("Heat lost per second while overheated. Keep it lower than Cool Rate.")]
    public float overheatCoolRate = 0.2f;
    [Tooltip("Heat the gun has to drop to before it can fire again. 0 = fully cooled.")]
    [Range(0f, 0.9f)] public float overheatRecoverAt = 0f;

    [Header("Overheat pose")]
    [Tooltip("How far the arm lifts while venting, in degrees.")]
    public float armLift = 50f;
    [Tooltip("Barrel angle above level while venting. 90 = pointing at the ceiling.")]
    public float barrelAngle = 90f;
    [Tooltip("How fast he moves into and out of the pose (full poses per second).")]
    public float poseSpeed = 6f;
    [Tooltip("Small wobble while venting, in degrees. 0 = off.")]
    public float ventShake = 2.5f;

    [Header("Sound (optional)")]
    public AudioClip overheatSound;
    public AudioClip cooledSound;

    public event Action Overheated;
    public event Action Cooled;

    public float Heat { get; private set; }
    public bool IsOverheated { get; private set; }
    public bool CanFire => !IsOverheated;

    WeaponSwap weapons;
    AudioSource audioSource;
    float sinceShot = float.MaxValue;
    float pose;

    void Awake()
    {
        weapons = GetComponent<WeaponSwap>();
        audioSource = GetComponent<AudioSource>();
    }

    public void AddShot() => AddHeat(heatPerShot);

    public void AddHeat(float amount)
    {
        if (IsOverheated || amount <= 0f) return;

        Heat = Mathf.Min(1f, Heat + amount);
        sinceShot = 0f;

        if (Heat >= 0.999f)
        {
            Heat = 1f;
            IsOverheated = true;
            Play(overheatSound);
            Overheated?.Invoke();
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        sinceShot += dt;

        if (IsOverheated)
        {
            Heat = Mathf.MoveTowards(Heat, 0f, overheatCoolRate * dt);
            if (Heat <= overheatRecoverAt)
            {
                IsOverheated = false;
                Play(cooledSound);
                Cooled?.Invoke();
            }
        }
        else if (sinceShot >= coolDelay)
        {
            Heat = Mathf.MoveTowards(Heat, 0f, coolRate * dt);
        }
    }

    // Runs after WeaponSwap places the gun and after BlasterAttack's recoil.
    void LateUpdate()
    {
        if (weapons.VisibleIndex != 1)
        {
            pose = 0f;
            return;
        }

        bool venting = IsOverheated && weapons.GunOut && !weapons.IsSwapping;
        pose = Mathf.MoveTowards(pose, venting ? 1f : 0f, poseSpeed * Time.deltaTime);
        if (pose <= 0f) return;

        float w = Mathf.SmoothStep(0f, 1f, pose);
        float shake = IsOverheated ? ventShake * w * (Mathf.PerlinNoise(Time.time * 9f, 0.37f) - 0.5f) * 2f : 0f;

        Transform arm = weapons.rightArm;
        Transform gun = weapons.gun != null ? weapons.gun.weapon : null;
        Quaternion lift = Quaternion.AngleAxis(-(armLift * w + shake), transform.right);

        if (arm != null) arm.rotation = lift * arm.rotation;

        if (gun != null)
        {
            Vector3 pivot = arm != null ? arm.position : gun.position;
            gun.position = pivot + lift * (gun.position - pivot);
            gun.rotation = Quaternion.AngleAxis(-(barrelAngle * w + shake), transform.right) * gun.rotation;
        }
    }

    void Play(AudioClip clip)
    {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
    }
}
