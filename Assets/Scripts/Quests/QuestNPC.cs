using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

/// Put this next to NPCInteractable on any NPC that's part of a quest.
/// It gives quests, advances steps when the player talks to this NPC, takes quest items,
/// and swaps in your quest dialogue. When it has nothing to say, the NPC's normal dialogue is used.
[DisallowMultipleComponent]
public class QuestNPC : MonoBehaviour
{
    [Tooltip("Name quest steps use to point at this NPC (Npc Id on a step). Leave empty to use the GameObject's name.")]
    public string questId;

    [Tooltip("Show a gold ! next to the NPC's name when they have something for the player.")]
    public bool showMarker = true;
    [Tooltip("Font for the NEW QUEST / QUEST UPDATED / QUEST COMPLETE popups (Kleader). " +
             "Only needs setting on one NPC (or on QuestLogUI). If empty, the Kleader font is found automatically.")]
    public TMP_FontAsset popupFont;

    [Header("Accepting quests")]
    [Tooltip("Off: the quest starts as soon as you talk to them. On: the dialogue says \"F to accept\" and the player chooses.")]
    public bool askToAccept = false;
    public Key acceptKey = Key.F;

    [Tooltip("This NPC's dialogue for each quest they're part of. Checked top to bottom.")]
    public List<QuestDialogue> quests = new List<QuestDialogue>();

    [System.Serializable]
    public class QuestDialogue
    {
        public QuestData quest;
        [Tooltip("This NPC hands out the quest.")]
        public bool givesQuest;

        [Tooltip("Said when offering the quest (before it starts).")]
        [TextArea(2, 5)] public string offerText;
        [Tooltip("Said right after the player accepts (only used when Ask To Accept is on).")]
        [TextArea(2, 5)] public string acceptedText;

        [Tooltip("What they say when the player talks to them on a step that's with this NPC. " +
                 "Step Number counts from 1, the same as the quest's step list.")]
        public List<StepLine> stepLines = new List<StepLine>();

        [Tooltip("Bring Item step, but the player doesn't have the item yet. e.g. \"Come back when you have my crown.\"")]
        [TextArea(2, 5)] public string missingItemText;
        [Tooltip("Quest is going, but nothing to do with this NPC right now. e.g. \"Did you talk to the miner yet?\"")]
        [TextArea(2, 5)] public string waitingText;
        [Tooltip("After the quest is finished.")]
        [TextArea(2, 5)] public string doneText;
    }

    [System.Serializable]
    public class StepLine
    {
        [Min(1)] public int stepNumber = 1;
        [TextArea(2, 5)] public string text;
    }

    /// Dialogue chosen for this conversation (empty = use the NPC's normal dialogue).
    public string Line { get; private set; }
    public bool HasLine => !string.IsNullOrEmpty(Line);
    /// A quest offer waiting for the player to press the accept key.
    public bool AcceptPending => pendingOffer != null;
    public string AcceptKeyName => acceptKey.ToString().ToUpper();
    public string Id => string.IsNullOrEmpty(questId) ? gameObject.name : questId;

    QuestDialogue pendingOffer;
    bool talking;
    NPCInteractable npc;

    void Awake()
    {
        npc = GetComponent<NPCInteractable>();
        if (popupFont) QuestManager.UIFont = popupFont;
    }

    // ------------------------------------------------------------------ called by NPCInteractable

    /// The player started talking: pick what to say and advance quests.
    public void OnInteract()
    {
        talking = true;
        Line = null;
        pendingOffer = null;
        string fallback = null;

        foreach (QuestDialogue d in quests)
        {
            if (d == null || d.quest == null) continue;
            QuestData q = d.quest;

            switch (QuestManager.GetState(q))
            {
                case QuestManager.State.NotStarted:
                    if (!d.givesQuest) break;
                    Line = d.offerText;
                    if (askToAccept) pendingOffer = d;
                    else QuestManager.StartQuest(q);
                    return;

                case QuestManager.State.Active:
                    QuestStep step = QuestManager.CurrentStepData(q);
                    int index = QuestManager.CurrentStep(q);
                    if (QuestManager.StepIsWith(step, Id))
                    {
                        if (step.type == QuestStepType.BringItem && step.item != null)
                        {
                            if (!QuestManager.RemoveItem(step.item, step.amount))
                            {
                                Line = !string.IsNullOrEmpty(d.missingItemText) ? d.missingItemText : d.waitingText;
                                return;
                            }
                        }
                        Line = StepText(d, index + 1);
                        if (string.IsNullOrEmpty(Line)) Line = d.waitingText;
                        QuestManager.CompleteStep(q);
                        return;
                    }
                    if (fallback == null && !string.IsNullOrEmpty(d.waitingText)) fallback = d.waitingText;
                    break;

                case QuestManager.State.Completed:
                    if (fallback == null && !string.IsNullOrEmpty(d.doneText)) fallback = d.doneText;
                    break;
            }
        }
        Line = fallback;
    }

    public void OnDialogueClosed()
    {
        talking = false;
        pendingOffer = null;
    }

    /// The name shown in the prompt bubble, with a gold ! when this NPC has something for the player.
    public string DecorateName(string npcName)
    {
        if (!showMarker || !HasSomethingForPlayer()) return npcName;
        return npcName + " <color=#F5C76B>!</color>";
    }

    public bool HasSomethingForPlayer()
    {
        foreach (QuestDialogue d in quests)
        {
            if (d == null || d.quest == null) continue;
            QuestManager.State s = QuestManager.GetState(d.quest);
            if (s == QuestManager.State.NotStarted && d.givesQuest) return true;
            if (s == QuestManager.State.Active && QuestManager.StepIsWith(QuestManager.CurrentStepData(d.quest), Id)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ accepting

    void Update()
    {
        if (!talking || pendingOffer == null || Keyboard.current == null) return;
        if (!Keyboard.current[acceptKey].wasPressedThisFrame) return;

        QuestDialogue d = pendingOffer;
        pendingOffer = null;
        QuestManager.StartQuest(d.quest);
        if (!string.IsNullOrEmpty(d.acceptedText)) Line = d.acceptedText;
        if (npc != null) npc.RefreshDialogue();
    }

    static string StepText(QuestDialogue d, int stepNumber)
    {
        foreach (StepLine l in d.stepLines)
            if (l != null && l.stepNumber == stepNumber) return l.text;
        return null;
    }
}