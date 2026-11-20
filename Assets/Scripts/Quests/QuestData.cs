using System.Collections.Generic;
using UnityEngine;

/// One quest: its name, description, steps and rewards.
/// Make one with right-click in the Project window > Create > Quests > Quest.
[CreateAssetMenu(fileName = "NewQuest", menuName = "Quests/Quest")]
public class QuestData : ScriptableObject
{
    [Tooltip("Shown in the quest log and the popups.")]
    public string questName = "New Quest";
    [TextArea(2, 5)]
    [Tooltip("Shown under the quest name in the quest log.")]
    public string description;

    [Tooltip("Done in order, top to bottom. Each step has its own line in the quest log.")]
    public List<QuestStep> steps = new List<QuestStep>();

    [Header("Rewards when finished (optional)")]
    public int moneyReward;
    public int reputationReward;
    public List<QuestItemReward> itemRewards = new List<QuestItemReward>();

}

public enum QuestStepType
{
    [Tooltip("Talk to an NPC.")]
    TalkTo,
    [Tooltip("Talk to an NPC while carrying an item. The item is handed over.")]
    BringItem,
    [Tooltip("Have an item in the inventory (picked up, from a loot crate, bought...).")]
    CollectItem,
    [Tooltip("Something happens in the world, e.g. a boss is defeated. Uses an Event Id.")]
    Event
}

[System.Serializable]
public class QuestStep
{
    [Tooltip("What the quest log says for this step, e.g. \"Ask the old miner about the noises in the mine.\"")]
    [TextArea(1, 3)] public string logText;

    public QuestStepType type = QuestStepType.TalkTo;

    [Tooltip("TalkTo / BringItem: the NPC's Quest Id (set on their QuestNPC component).")]
    public string npcId;

    [Tooltip("CollectItem / BringItem: the item needed.")]
    public ItemSO item;
    [Min(1)] public int amount = 1;

    [Tooltip("Event: the event that finishes this step, e.g. QueenNibbler (the boss sends this when defeated).")]
    public string eventId;
}

[System.Serializable]
public class QuestItemReward
{
    public ItemSO item;
    [Min(1)] public int amount = 1;
}