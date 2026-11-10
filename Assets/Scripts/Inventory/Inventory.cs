using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class Inventory : MonoBehaviour
{
    public Transform player;
    public PlayerInteract playerInteract;

    public GameObject inventorySlotParent;
    public GameObject container;

    public float pickupRange = 3f;
    public Material highlightMaterial;

    [Header("Coins")]
    [Tooltip("Your Coins ItemSO. One slot always shows the player's money (PlayerStats), so loot crates, " +
             "the HUD and shops all use the same amount. Leave empty to not show coins in the inventory.")]
    public ItemSO coinItem;

    // Items are kept here between scenes (town -> mine -> town), so changing scenes doesn't erase them.
    // Cleared when you stop Play / quit the game.
    private static ItemSO[] savedItems;
    private static int[] savedAmounts;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSavedItems()
    {
        savedItems = null;
        savedAmounts = null;
    }

    private Item lookedAtItem;

    private Renderer[] lookedAtRenderers;
    private Material[][] originalMaterials;
    private Material[][] highlightMaterials;

    private List<Slot> inventorySlots = new List<Slot>();

    private void Awake()
    {
        if (playerInteract == null)
            playerInteract = FindFirstObjectByType<PlayerInteract>();

        if (player == null && playerInteract != null)
            player = playerInteract.transform;

        if (player == null || container == null ||
            inventorySlotParent == null)
        {
            Debug.LogError("Inventory is missing Inspector references!");
            enabled = false;
            return;
        }

        inventorySlots.AddRange(
            inventorySlotParent.GetComponentsInChildren<Slot>(true)
        );

        LoadSavedItems();

        Debug.Log("Inventory ready. Total slots: " + inventorySlots.Count);
    }

    // ---------------------------------------------------------------- carry over between scenes

    private void LoadSavedItems()
    {
        if (savedItems == null) return;

        int count = Mathf.Min(savedItems.Length, inventorySlots.Count);
        for (int i = 0; i < count; i++)
        {
            if (savedItems[i] != null && savedAmounts[i] > 0)
                inventorySlots[i].SetItem(savedItems[i], savedAmounts[i]);
            else
                inventorySlots[i].ClearSlot();
        }
    }

    private void SaveItems()
    {
        if (inventorySlots.Count == 0) return;

        if (savedItems == null || savedItems.Length != inventorySlots.Count)
        {
            savedItems = new ItemSO[inventorySlots.Count];
            savedAmounts = new int[inventorySlots.Count];
        }

        for (int i = 0; i < inventorySlots.Count; i++)
        {
            Slot slot = inventorySlots[i];
            bool has = slot != null && slot.HasItem();
            savedItems[i] = has ? slot.GetItem() : null;
            savedAmounts[i] = has ? slot.GetAmount() : 0;
        }
    }

    private void OnDisable() { SaveItems(); }

    // ---------------------------------------------------------------- coins

    // Keeps one slot showing the player's money. Coins live in PlayerStats; this slot just displays them.
    private void SyncCoins()
    {
        if (coinItem == null || PlayerStats.Instance == null) return;

        int money = PlayerStats.Instance.Money;
        Slot coinSlot = null;

        foreach (Slot slot in inventorySlots)
        {
            if (slot.HasItem() && slot.GetItem() == coinItem)
            {
                if (coinSlot == null) coinSlot = slot;
                else slot.ClearSlot(); // only ever one coin slot
            }
        }

        if (money <= 0)
        {
            if (coinSlot != null) coinSlot.ClearSlot();
            return;
        }

        if (coinSlot == null)
        {
            foreach (Slot slot in inventorySlots)
            {
                if (!slot.HasItem()) { coinSlot = slot; break; }
            }
            if (coinSlot == null) return; // inventory full; coins are still counted in PlayerStats
        }

        if (coinSlot.GetAmount() != money)
            coinSlot.SetItem(coinItem, money);
    }

    private void Update()
    {
        SyncCoins();
        SaveItems();

        if (Keyboard.current == null)
            return;

        if (container.activeSelf)
        {
            ClearCurrentItem();
            return;
        }

        DetectLookedAtItem();

        // TEMPORARY HEALTH TEST
        if (Keyboard.current.kKey.wasPressedThisFrame)
        {
            if (PlayerStats.Instance != null)
            {
                PlayerStats.Instance.SetHealth(
                    PlayerStats.Instance.Health - 25f
                );

                Debug.Log("Test damage: " + PlayerStats.Instance.Health);
            }
        }

        if (playerInteract == null || !playerInteract.IsInteracting)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame)
                UseInventoryItem(0);

            if (Keyboard.current.digit2Key.wasPressedThisFrame)
                UseInventoryItem(1);

            if (Keyboard.current.digit3Key.wasPressedThisFrame)
                UseInventoryItem(2);

            if (Keyboard.current.digit4Key.wasPressedThisFrame)
                UseInventoryItem(3);

            if (Keyboard.current.digit5Key.wasPressedThisFrame)
                UseInventoryItem(4);

            if (Keyboard.current.digit6Key.wasPressedThisFrame)
                UseInventoryItem(5);

            Pickup();
        }
    }

    private void UseInventoryItem(int index)
    {
        if (index < 0 || index >= inventorySlots.Count)
        {
            Debug.Log("There is no inventory slot at that number!");
            return;
        }

        Slot slot = inventorySlots[index];

        // Check if the slot is empty
        if (slot == null || !slot.HasItem())
        {
            Debug.Log("There is no item in inventory slot " + (index + 1) + "!");
            return;
        }

        ItemSO item = slot.GetItem();

        if (item == null)
        {
            Debug.Log("There is no item in inventory slot " + (index + 1) + "!");
            return;
        }

        // Only health potions can be used right now
        if (!item.isHealthPotion)
        {
            Debug.Log(item.itemName + " cannot be used!");
            return;
        }

        if (PlayerStats.Instance == null)
        {
            Debug.Log("PlayerStats instance not found!");
            return;
        }

        if (PlayerStats.Instance.Health >= PlayerStats.Instance.MaxHealth)
        {
            Debug.Log("Health is already full!");
            return;
        }

        // Use the potion
        slot.UseItem();

        // Show updated health
        Debug.Log(
            "Used " + item.itemName +
            "! Health: " +
            PlayerStats.Instance.Health +
            "/" +
            PlayerStats.Instance.MaxHealth
        );
    }

    private void DetectLookedAtItem()
    {
        if (player == null)
            return;

        Collider[] nearbyObjects = Physics.OverlapSphere(
            player.position,
            pickupRange,
            ~0,
            QueryTriggerInteraction.Collide
        );

        Item closestItem = null;
        float closestDistance = pickupRange;

        foreach (Collider col in nearbyObjects)
        {
            Item item = col.GetComponentInParent<Item>();

            if (item == null || item.item == null)
                continue;

            float distance = Vector3.Distance(
                player.position,
                item.transform.position
            );

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestItem = item;
            }
        }

        if (closestItem == lookedAtItem)
            return;

        ClearCurrentItem();

        lookedAtItem = closestItem;

        if (lookedAtItem != null)
        {
            lookedAtItem.ShowPickupPrompt();
            ApplyHighlight();
        }
    }

    private void ApplyHighlight()
    {
        if (lookedAtItem == null ||
            lookedAtItem.visualModel == null ||
            highlightMaterial == null)
            return;

        lookedAtRenderers =
            lookedAtItem.visualModel.GetComponentsInChildren<Renderer>(true);

        originalMaterials = new Material[lookedAtRenderers.Length][];
        highlightMaterials = new Material[lookedAtRenderers.Length][];

        for (int i = 0; i < lookedAtRenderers.Length; i++)
        {
            originalMaterials[i] = lookedAtRenderers[i].sharedMaterials;

            highlightMaterials[i] =
                new Material[originalMaterials[i].Length];

            for (int j = 0; j < highlightMaterials[i].Length; j++)
            {
                highlightMaterials[i][j] = highlightMaterial;
            }

            lookedAtRenderers[i].sharedMaterials = highlightMaterials[i];
        }
    }

    private void ClearHighlight()
    {
        if (lookedAtRenderers != null && originalMaterials != null)
        {
            for (int i = 0; i < lookedAtRenderers.Length; i++)
            {
                if (lookedAtRenderers[i] != null)
                    lookedAtRenderers[i].sharedMaterials = originalMaterials[i];
            }
        }

        lookedAtRenderers = null;
        originalMaterials = null;
        highlightMaterials = null;
    }

    private void ClearCurrentItem()
    {
        ClearHighlight();

        if (lookedAtItem != null)
            lookedAtItem.HidePickupPrompt();

        lookedAtItem = null;
    }

    private void Pickup()
    {
        if (lookedAtItem == null)
            return;

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            Item item = lookedAtItem;

            Debug.Log("Picking up: " + item.item.itemName);

            ClearCurrentItem();

            AddItem(item.item, item.amount);

            Destroy(item.gameObject);
        }
    }

    public void AddItem(ItemSO itemToAdd, int amount)
    {
        // Coins picked up anywhere go to the player's money; the coin slot shows it.
        if (itemToAdd != null && itemToAdd == coinItem)
        {
            if (PlayerStats.Instance != null) PlayerStats.Instance.AddMoney(amount);
            SyncCoins();
            return;
        }

        int remaining = amount;

        // Stack existing items first
        foreach (Slot slot in inventorySlots)
        {
            if (slot.HasItem() && slot.GetItem() == itemToAdd)
            {
                int spaceLeft = itemToAdd.maxStackSize - slot.GetAmount();
                int amountToAdd = Mathf.Min(spaceLeft, remaining);

                if (amountToAdd > 0)
                {
                    slot.SetItem(itemToAdd, slot.GetAmount() + amountToAdd);
                    remaining -= amountToAdd;
                }

                if (remaining <= 0)
                    return;
            }
        }

        // Put remaining items in empty inventory slots
        foreach (Slot slot in inventorySlots)
        {
            if (!slot.HasItem())
            {
                int amountToPlace = Mathf.Min(
                    itemToAdd.maxStackSize,
                    remaining
                );

                slot.SetItem(itemToAdd, amountToPlace);
                remaining -= amountToPlace;

                if (remaining <= 0)
                    return;
            }
        }

        if (remaining > 0)
        {
            Debug.LogWarning(
                "Not enough inventory space. Remaining: " + remaining
            );
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (player == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(player.position, pickupRange);
    }
}



//using UnityEngine;
//using System.Collections.Generic;
//using UnityEngine.InputSystem;

//public class Inventory : MonoBehaviour
//{
//    public Transform player;
//    public PlayerInteract playerInteract;

//    public GameObject hotbarObj;
//    public GameObject inventorySlotParent;
//    public GameObject container;

//    public float pickupRange = 3f;
//    public Material highlightMaterial;

//    private Item lookedAtItem;
//    private Renderer lookedAtRenderer;
//    private Material originalMaterial;

//    private List<Slot> inventorySlots = new List<Slot>();
//    private List<Slot> hotbarSlots = new List<Slot>();
//    private List<Slot> allSlots = new List<Slot>();

//    private void Awake()
//    {
//        // Find Player automatically if not assigned
//        if (playerInteract == null)
//            playerInteract = FindFirstObjectByType<PlayerInteract>();

//        if (player == null && playerInteract != null)
//            player = playerInteract.transform;

//        // Check required references
//        if (player == null || container == null ||
//            hotbarObj == null || inventorySlotParent == null)
//        {
//            Debug.LogError("Inventory is missing Inspector references!");
//            enabled = false;
//            return;
//        }

//        inventorySlots.AddRange(
//            inventorySlotParent.GetComponentsInChildren<Slot>(true)
//        );

//        hotbarSlots.AddRange(
//            hotbarObj.GetComponentsInChildren<Slot>(true)
//        );

//        allSlots.AddRange(inventorySlots);
//        allSlots.AddRange(hotbarSlots);

//        Debug.Log("Inventory ready. Total slots: " + allSlots.Count);
//    }

//    void Update()
//    {
//        if (Keyboard.current == null)
//            return;

//        // Don't pick up items while inventory is open
//        if (container.activeSelf)
//            return;

//        DetectLookedAtItem();

//        if (playerInteract == null || !playerInteract.IsInteracting)
//        {
//            Pickup();
//        }
//    }

//    //private void OnEnable()
//    //{
//    //    Debug.LogWarning("INVENTORY ENABLED");
//    //}

//    //private void OnDisable()
//    //{
//    //    Debug.LogWarning("INVENTORY DISABLED");
//    //}

//    //void Update()
//    //{
//    //    Debug.LogWarning("INVENTORY UPDATE IS RUNNING");

//    //    if (Keyboard.current == null)
//    //        return;

//    //    if (Keyboard.current.iKey.wasPressedThisFrame)
//    //    {
//    //        container.SetActive(!container.activeSelf);

//    //        Cursor.lockState = container.activeSelf
//    //            ? CursorLockMode.None
//    //            : CursorLockMode.Locked;

//    //        Cursor.visible = container.activeSelf;
//    //    }

//    //    if (container.activeSelf)
//    //        return;

//    //    DetectLookedAtItem();

//    //    if (playerInteract == null || !playerInteract.IsInteracting)
//    //        Pickup();
//    //}

//    //void Update()
//    //{
//    //    Debug.Log("INVENTORY UPDATE RUNNING");

//    //    if (Keyboard.current == null)
//    //        return;

//    //    // Open and close inventory
//    //    if (Keyboard.current.iKey.wasPressedThisFrame)
//    //    {
//    //        container.SetActive(!container.activeSelf);

//    //        Cursor.lockState = container.activeSelf
//    //            ? CursorLockMode.None
//    //            : CursorLockMode.Locked;

//    //        Cursor.visible = container.activeSelf;
//    //    }

//    //    // Don't pick up items while inventory is open
//    //    if (container.activeSelf)
//    //        return;

//    //    // Find nearby items
//    //    DetectLookedAtItem();

//    //    // Don't pick up while interacting with an NPC
//    //    if (playerInteract == null || !playerInteract.IsInteracting)
//    //    {
//    //        Pickup();
//    //    }
//    //}

//    private float nextDebugTime = 0f;

//    private void DetectLookedAtItem()
//    {
//        if (player == null)
//        {
//            Debug.LogError("INVENTORY: Player reference is missing!");
//            return;
//        }

//        Collider[] nearbyObjects = Physics.OverlapSphere(
//            player.position,
//            pickupRange,
//            ~0,
//            QueryTriggerInteraction.Collide
//        );

//        Item closestItem = null;
//        float closestDistance = pickupRange;

//        foreach (Collider col in nearbyObjects)
//        {
//            Item item = col.GetComponentInParent<Item>();

//            if (Time.time >= nextDebugTime)
//            {
//                Debug.Log("Nearby collider: " + col.name +
//                          " | Item found: " + (item != null));
//            }

//            if (item == null)
//                continue;

//            float distance = Vector3.Distance(
//                player.position,
//                item.transform.position
//            );

//            if (distance < closestDistance)
//            {
//                closestDistance = distance;
//                closestItem = item;
//            }
//        }

//        if (Time.time >= nextDebugTime)
//        {
//            Debug.Log("Player position: " + player.position);
//            Debug.Log("Nearby colliders: " + nearbyObjects.Length);
//            Debug.Log("Closest item: " +
//                      (closestItem != null ? closestItem.name : "NONE"));

//            nextDebugTime = Time.time + 1f;
//        }

//        //if (closestItem == lookedAtItem)
//        //    return;

//        //ClearHighlight();
//        //lookedAtItem = closestItem;
//        if (closestItem == lookedAtItem)
//            return;

//        ClearHighlight();

//        if (lookedAtItem != null)
//            lookedAtItem.HidePickupPrompt();

//        lookedAtItem = closestItem;

//        if (lookedAtItem != null)
//        {
//            lookedAtItem.ShowPickupPrompt();

//            lookedAtRenderer =
//                lookedAtItem.GetComponentInChildren<Renderer>(true);

//            if (lookedAtRenderer != null && highlightMaterial != null)
//            {
//                originalMaterial = lookedAtRenderer.material;
//                lookedAtRenderer.material = highlightMaterial;
//            }
//        }

//        if (lookedAtItem != null)
//        {
//            lookedAtRenderer =
//                lookedAtItem.GetComponentInChildren<Renderer>(true);

//            if (lookedAtRenderer != null && highlightMaterial != null)
//            {
//                originalMaterial = lookedAtRenderer.material;
//                lookedAtRenderer.material = highlightMaterial;
//            }
//        }
//    }

//    private void ClearHighlight()
//    {
//        if (lookedAtRenderer != null && originalMaterial != null)
//        {
//            lookedAtRenderer.material = originalMaterial;
//        }

//        lookedAtRenderer = null;
//        originalMaterial = null;
//    }

//    private void Pickup()
//    {
//        if (lookedAtItem == null)
//            return;

//        if (Keyboard.current.eKey.wasPressedThisFrame)
//        {
//            Item item = lookedAtItem;

//            Debug.Log("Picking up: " + item.item.itemName);

//            ClearHighlight();

//            item.HidePickupPrompt();
//            lookedAtItem = null;
//            //ClearHighlight();
//            //lookedAtItem = null;

//            AddItem(item.item, item.amount);

//            Destroy(item.gameObject);
//        }
//    }

//    public void AddItem(ItemSO itemToAdd, int amount)
//    {
//        int remaining = amount;

//        // Stack existing items first
//        foreach (Slot slot in allSlots)
//        {
//            if (slot.HasItem() && slot.GetItem() == itemToAdd)
//            {
//                int currentAmount = slot.GetAmount();
//                int maxStack = itemToAdd.maxStackSize;

//                if (currentAmount < maxStack)
//                {
//                    int spaceLeft = maxStack - currentAmount;
//                    int amountToAdd = Mathf.Min(spaceLeft, remaining);

//                    slot.SetItem(itemToAdd, currentAmount + amountToAdd);
//                    remaining -= amountToAdd;

//                    if (remaining <= 0)
//                        return;
//                }
//            }
//        }

//        // Put remaining items in empty slots
//        foreach (Slot slot in allSlots)
//        {
//            if (!slot.HasItem())
//            {
//                int amountToPlace = Mathf.Min(
//                    itemToAdd.maxStackSize,
//                    remaining
//                );

//                slot.SetItem(itemToAdd, amountToPlace);
//                remaining -= amountToPlace;

//                if (remaining <= 0)
//                    return;
//            }
//        }

//        if (remaining > 0)
//        {
//            Debug.LogWarning(
//                "Not enough inventory space. Remaining: " + remaining
//            );
//        }
//    }

//    private void OnDrawGizmosSelected()
//    {
//        if (player == null)
//            return;

//        Gizmos.color = Color.yellow;
//        Gizmos.DrawWireSphere(player.position, pickupRange);
//    }
//}






//using UnityEngine;
//using System.Collections.Generic;
//using UnityEngine.InputSystem;

//public class Inventory : MonoBehaviour
//{
//    public ItemSO medicineItem;
//    public ItemSO weaponItem;

//    public Transform player;
//    public PlayerInteract playerInteract;

//    public GameObject hotbarObj;
//    public GameObject inventorySlotParent;
//    public GameObject container;

//    //public float pickupRange = 3f;
//    //private Item lookedAtItem = null;
//    //public Material highlightMaterial;
//    //private Material originalMaterial;
//    //private Renderer lookedAtRenderer = null;

//    public float pickupRange = 3f;

//    private Item lookedAtItem = null;
//    public Material highlightMaterial;
//    private Material originalMaterial;
//    private Renderer lookedAtRenderer = null;

//    private List<Slot> inventorySlots = new List<Slot>();
//    private List<Slot> hotbarSlots = new List<Slot>();
//    private List<Slot> allSlots = new List<Slot>();

//    private void Awake()
//    {
//        inventorySlots.AddRange(inventorySlotParent.GetComponentsInChildren<Slot>());
//        hotbarSlots.AddRange(hotbarObj.GetComponentsInChildren<Slot>());

//        allSlots.AddRange(inventorySlots);
//        allSlots.AddRange(hotbarSlots);

//        Debug.Log("Inventory slots: " + inventorySlots.Count);
//        Debug.Log("Hotbar slots: " + hotbarSlots.Count);
//        Debug.Log("Total slots: " + allSlots.Count);
//    }

//    void Update()
//    {
//        if (Keyboard.current == null)
//            return;

//        if (Keyboard.current.iKey.wasPressedThisFrame)
//        {
//            container.SetActive(!container.activeInHierarchy);

//            Cursor.lockState = container.activeInHierarchy
//                ? CursorLockMode.None
//                : CursorLockMode.Locked;

//            Cursor.visible = container.activeInHierarchy;
//        }

//        // Don't pick up items while inventory is open
//        if (container.activeInHierarchy)
//            return;

//        DetectLookedAtItem();
//        DetectLookedAtItem();

//        if (playerInteract == null || !playerInteract.IsInteracting)
//        {
//            Pickup();
//        }
//    }

//    //void Update()
//    //{
//    //    if (Keyboard.current == null)
//    //        return;

//    //    //if (Keyboard.current.mKey.wasPressedThisFrame)
//    //    //{
//    //    //    AddItem(medicineItem, 3);
//    //    //}
//    //    //else if (Keyboard.current.wKey.wasPressedThisFrame)
//    //    //{
//    //    //    AddItem(weaponItem, 1);
//    //    //}

//    //    if(Input.GetKeyDown(KeyCode.I))
//    //    {
//    //        container.SetActive(!container.activeInHierarchy);
//    //        Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
//    //        Cursor.visible = !Cursor.visible;
//    //    }

//    //    DetectLookedAtItem();
//    //    Pickup();
//    //}

//    //void Update()
//    //{
//    //    if (Input.GetKeyDown(KeyCode.M))
//    //    {
//    //        AddItem(medicineItem, 3);
//    //    }
//    //    else if (Input.GetKeyDown(KeyCode.W))
//    //    {
//    //        AddItem(weaponItem, 1);
//    //    }
//    //}

//    public void AddItem(ItemSO itemToAdd, int amount)
//    {
//        int remaining = amount;

//        foreach(Slot slot in allSlots)
//        {
//            if(slot.HasItem() && slot.GetItem() == itemToAdd)
//            {
//                int currentAmount = slot.GetAmount();
//                int maxStack = itemToAdd.maxStackSize;

//                if(currentAmount < maxStack)
//                {
//                    int spaceLeft = maxStack - currentAmount;
//                    int amountToAdd = Mathf.Min(spaceLeft, remaining);

//                    slot.SetItem(itemToAdd, currentAmount + amountToAdd);
//                    remaining -= amountToAdd;

//                    if (remaining <= 0)
//                    {
//                        return;
//                    }

//                }
//            }
//        }

//        foreach(Slot slot in allSlots)
//        {
//            if (!slot.HasItem())
//            {
//                int amountToPlace = Mathf.Min(itemToAdd.maxStackSize, remaining);
//                slot.SetItem(itemToAdd, amountToPlace);
//                remaining -= amountToPlace;

//                if (remaining <= 0)
//                {
//                    return;
//                }
//            }
//        }

//        if(remaining > 0)
//        {
//            Debug.LogWarning("Not enough space in inventory to add all items. Remaining: " + remaining + " of " + itemToAdd.itemName);
//        }
//    }

//    private void Pickup()
//    {
//        if (lookedAtItem != null &&
//            Keyboard.current.eKey.wasPressedThisFrame)
//        {
//            Item item = lookedAtItem;

//            if (lookedAtRenderer != null)
//                lookedAtRenderer.material = originalMaterial;

//            lookedAtItem = null;
//            lookedAtRenderer = null;
//            originalMaterial = null;

//            AddItem(item.item, item.amount);
//            Destroy(item.gameObject);
//        }
//    }

//    //private void Pickup()
//    //{
//    //    if(lookedAtRenderer != null && Input.GetKeyDown(KeyCode.E))
//    //    {
//    //        //AddItem item = lookedAtRenderer.GetComponent<item>();
//    //        Item item = lookedAtRenderer.GetComponent<Item>();
//    //        if (item != null)
//    //        {
//    //            AddItem(item.item, item.amount);
//    //            Destroy(item.gameObject);
//    //        }
//    //    }
//    //}

//    private void DetectLookedAtItem()
//    {
//        Collider[] nearbyObjects = Physics.OverlapSphere(
//            player.position,
//            pickupRange
//        );

//        Item closestItem = null;
//        float closestDistance = pickupRange;

//        foreach (Collider col in nearbyObjects)
//        {
//            Item item = col.GetComponentInParent<Item>();

//            if (item == null)
//                continue;

//            float distance = Vector3.Distance(
//                player.position,
//                item.transform.position
//            );

//            if (distance < closestDistance)
//            {
//                closestDistance = distance;
//                closestItem = item;
//            }
//        }

//        if (closestItem == lookedAtItem)
//            return;

//        // Remove previous highlight
//        if (lookedAtRenderer != null)
//            lookedAtRenderer.material = originalMaterial;

//        lookedAtItem = closestItem;
//        lookedAtRenderer = null;
//        originalMaterial = null;

//        // Highlight new item
//        if (lookedAtItem != null)
//        {
//            lookedAtRenderer = lookedAtItem.GetComponentInChildren<Renderer>();

//            if (lookedAtRenderer != null)
//            {
//                originalMaterial = lookedAtRenderer.material;
//                lookedAtRenderer.material = highlightMaterial;
//            }
//        }
//    }

//    //private void DetectLookedAtItem()
//    //{
//    //    if(lookedAtRenderer != null)
//    //    {
//    //        lookedAtRenderer.material = originalMaterial;
//    //        lookedAtRenderer = null;
//    //        originalMaterial = null;
//    //    }

//    //    Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
//    //    if(Physics.Raycast(ray, out RaycastHit hit, pickupRange))
//    //    {
//    //        //AddItem item = hit.collider.GetComponent<Item>();
//    //        Item item = hit.collider.GetComponent<Item>();
//    //        if (item != null)
//    //        {
//    //            //lookedAtRenderer rend = item.GetComponent<Renderer>();
//    //            Renderer rend = item.GetComponent<Renderer>();
//    //            if (rend != null)
//    //            {
//    //                originalMaterial = rend.material;
//    //                rend.material = highlightMaterial;
//    //                lookedAtRenderer = rend;
//    //            }
//    //        }
//    //    }

//    //}
//}