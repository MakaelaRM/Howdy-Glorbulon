//using UnityEngine;

//[CreateAssetMenu(fileName = "Item", menuName = "NewItem")]

//public class ItemSO : ScriptableObject
//{

//    public string itemName;
//    public Sprite icon;
//    public int maxStateSize;
//    public GameObject itemPrefab;
//    public GameObject handItemPrefab;
//}

using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "NewItem")]
public class ItemSO : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    public int maxStackSize;
    public GameObject itemPrefab;
    public GameObject handItemPrefab;

    [Header("Consumable Settings")]
    public bool isHealthPotion;
    public float healingAmount = 25f;
}