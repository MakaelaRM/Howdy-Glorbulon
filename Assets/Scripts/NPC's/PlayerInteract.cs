using UnityEngine;
using UnityEngine.InputSystem;

/// Finds the nearest NPC, shows its prompt, and runs dialogue and shop interactions with it.
public class PlayerInteract : MonoBehaviour
{
    [SerializeField] private InputAction interactAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
    [SerializeField] private InputAction shopAction = new InputAction("OpenShop", InputActionType.Button, "<Keyboard>/q");
    [SerializeField] private float interactRange = 5f;
    [SerializeField] private FixedAngleCamera cameraController;

    [Header("Shop")]
    [SerializeField] private ShopMenu shopMenu;
    [Tooltip("How far in front of the NPC the camera stops while shopping.")]
    [SerializeField] private float shopFocusDistance = 3f;
    [Tooltip("Where the NPC sits across the screen while shopping. 0.75 = centered on the right half.")]
    [SerializeField, Range(0.5f, 0.95f)] private float shopScreenX = 0.75f;

    private NPCInteractable currentNPC;
    private NPCInteractable interactingNPC;
    private bool inShop;
    private bool contentShown;
    private bool promptShowsShop;

    public bool IsInteracting => interactingNPC != null;

    void Awake()
    {
        if (cameraController == null && Camera.main != null)
            cameraController = Camera.main.GetComponent<FixedAngleCamera>();
        if (shopMenu == null)
            shopMenu = FindFirstObjectByType<ShopMenu>();
    }

    void OnEnable()
    {
        interactAction.Enable();
        shopAction.Enable();
    }

    void OnDisable()
    {
        interactAction.Disable();
        shopAction.Disable();
    }

    void Update()
    {
        if (IsInteracting)
        {
            if (!contentShown && (cameraController == null || cameraController.IsFocusComplete))
            {
                if (inShop)
                {
                    if (shopMenu != null) shopMenu.Open(interactingNPC, shopAction.GetBindingDisplayString());
                }
                else
                {
                    Transform viewer = cameraController != null ? cameraController.transform : Camera.main.transform;
                    interactingNPC.ShowDialogue(interactAction.GetBindingDisplayString(), viewer);
                }
                contentShown = true;
            }

            if (!inShop && interactAction.WasPressedThisFrame())
            {
                // PAGES: E shows the next page of dialogue; on the last page it ends the conversation
                if (!contentShown || !interactingNPC.NextPage()) EndInteraction();
            }
            else if (inShop && shopAction.WasPressedThisFrame())
                EndInteraction();
            return;
        }

        NPCInteractable closest = FindClosestNPC();

        if (closest != currentNPC)
        {
            if (currentNPC != null) currentNPC.HidePrompt();
            currentNPC = closest;
            if (currentNPC != null) ShowPrompt(currentNPC);
        }
        else if (currentNPC != null && currentNPC.CanOpenShop != promptShowsShop)
        {
            ShowPrompt(currentNPC);
        }

        if (currentNPC == null) return;

        if (interactAction.WasPressedThisFrame())
            StartInteraction(currentNPC, false);
        else if (currentNPC.CanOpenShop && shopAction.WasPressedThisFrame())
            StartInteraction(currentNPC, true);
    }

    private void ShowPrompt(NPCInteractable npc)
    {
        promptShowsShop = npc.CanOpenShop;
        npc.ShowPrompt(interactAction.GetBindingDisplayString(), shopAction.GetBindingDisplayString());
    }

    private void StartInteraction(NPCInteractable npc, bool shop)
    {
        if (shop && shopMenu == null)
        {
            Debug.LogWarning("No ShopMenu in the scene. Add one to an empty GameObject to use shops.", this);
            return;
        }

        interactingNPC = npc;
        inShop = shop;
        contentShown = false;
        npc.HidePrompt();

        if (shop)
        {
            npc.EnterShop();
            if (cameraController != null)
                cameraController.FocusOnOffset(npc.transform, npc.FocusPoint, shopScreenX, shopFocusDistance);
        }
        else
        {
            npc.Interact();
            if (cameraController != null)
                cameraController.FocusOn(npc.transform, npc.FocusPoint);
        }
    }

    private void EndInteraction()
    {
        if (inShop)
        {
            if (shopMenu != null) shopMenu.Close();
            interactingNPC.ExitShop();
        }
        else
        {
            interactingNPC.HideDialogue();
        }

        interactingNPC = null;
        currentNPC = null;
        inShop = false;
        contentShown = false;

        if (cameraController != null)
            cameraController.StopFocus();
    }

    private NPCInteractable FindClosestNPC()
    {
        NPCInteractable closest = null;
        float closestDistance = Mathf.Infinity;

        Collider[] colliderArray = Physics.OverlapSphere(transform.position, interactRange);
        foreach (Collider collider in colliderArray)
        {
            if (collider.TryGetComponent(out NPCInteractable npc))
            {
                float distance = Vector3.Distance(transform.position, npc.transform.position);

                if (distance <= interactRange && distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = npc;
                }
            }
        }
        return closest;
    }
}