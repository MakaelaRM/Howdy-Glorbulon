using UnityEngine;

public class Footsteps : MonoBehaviour
{
    private AudioSource audioSource;
    [SerializeField] private AudioClip footstepClip;

    [SerializeField, Range(0f, 1f)] private float footstepVolume = 1f;

    [SerializeField] private float stepInterval = 1.25f;
    private float stepTimer;

    private CharacterController characterController;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        if (characterController.isGrounded && characterController.velocity.magnitude > 0.1f)
        {
            stepTimer -= Time.deltaTime;

            if (stepTimer <= 0)
            {
                PlayRandomFootstep();
                stepTimer = stepInterval;
            }
        }
        else
        {
            stepTimer = 0.5f;
        }
    }

    void PlayRandomFootstep()
    {
        audioSource.PlayOneShot(footstepClip, footstepVolume);
    }
}