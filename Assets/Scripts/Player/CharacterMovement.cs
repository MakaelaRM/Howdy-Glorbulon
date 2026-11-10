using UnityEngine;
using UnityEngine.InputSystem;

public class CharacterMovement : MonoBehaviour
{
    private float playerSpeed = 4f;
    private float sprintMultiplier = 1.8f;
    private float jumpHeight = .8f;
    private float gravityValue = -3f;
    private float turnSpeed = 10f;

    public CharacterController controller;
    public GameObject inventoryUI;

    public enum FacingMode { WalkDirection_AimWhenAttacking, AlwaysFaceAim }
    [Tooltip("WalkDirection_AimWhenAttacking: face where you walk; turn to the mouse when you attack (and when standing still).\nAlwaysFaceAim: always face the mouse (strafing).")]
    public FacingMode facing = FacingMode.WalkDirection_AimWhenAttacking;
    [Tooltip("When standing still, turn to face the mouse.")]
    public bool faceAimWhenIdle = true;
    [Tooltip("How fast the player turns to face the aim.")]
    public float aimTurnSpeed = 20f;

    private Vector3 playerVelocity;
    private bool groundedPlayer;
    private bool inventoryOpen;

    [Header("Input Actions")]
    public InputActionReference moveAction;
    public InputActionReference jumpAction;
    public InputActionReference sprintAction;

    private void Start()
    {
        // Make sure the inventory starts closed.
        if (inventoryUI != null) inventoryUI.SetActive(false);
    }

    private void OnEnable()
    {
        moveAction.action.Enable();
        jumpAction.action.Enable();
        sprintAction.action.Enable();
    }

    private void OnDisable()
    {
        moveAction.action.Disable();
        jumpAction.action.Disable();
        sprintAction.action.Disable();
    }

    void Update()
    {
        // Toggle inventory when I is pressed.
        if (Keyboard.current != null &&
            Keyboard.current.iKey.wasPressedThisFrame &&
            inventoryUI != null)
        {
            inventoryOpen = !inventoryOpen;
            inventoryUI.SetActive(inventoryOpen);
        }

        // Stop all player movement while inventory is open.
        if (inventoryOpen)
        {
            return;
        }

        groundedPlayer = controller.isGrounded;

        if (groundedPlayer)
        {
            if (playerVelocity.y < -2f)
                playerVelocity.y = -2f;
        }

        Vector2 input = moveAction.action.ReadValue<Vector2>();
        Vector3 move = new Vector3(input.x, 0, input.y);
        move = Vector3.ClampMagnitude(move, 1f);

        // Facing
        PlayerAim aim = PlayerAim.Instance;
        Vector3 face = Vector3.zero;
        float speed = turnSpeed;
        if (aim != null && (facing == FacingMode.AlwaysFaceAim || aim.HoldFacing))
        {
            face = aim.FacingDirection; speed = aimTurnSpeed;        // attacking (or strafe mode): face the aim
        }
        else if (move != Vector3.zero)
        {
            face = move;                                             // walking: face where you walk
        }
        else if (aim != null && faceAimWhenIdle)
        {
            face = aim.FacingDirection; speed = aimTurnSpeed * 0.5f; // standing still: ease toward the aim
        }

        if (face != Vector3.zero)
        {
            controller.transform.rotation = Quaternion.Slerp(
                controller.transform.rotation,
                Quaternion.LookRotation(face),
                speed * Time.deltaTime);
        }

        if (groundedPlayer &&
            jumpAction.action.WasPressedThisFrame())
        {
            playerVelocity.y = Mathf.Sqrt(
                jumpHeight * -2f * gravityValue);
        }

        playerVelocity.y += gravityValue * Time.deltaTime;

        bool isSprinting = sprintAction.action.IsPressed();

        float currentSpeed = isSprinting
            ? playerSpeed * sprintMultiplier
            : playerSpeed;

        Vector3 finalMove =
            move * currentSpeed + Vector3.up * playerVelocity.y;

        controller.Move(finalMove * Time.deltaTime);
    }
}