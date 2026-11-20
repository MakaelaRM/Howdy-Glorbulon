using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// Keeps track of every quest's progress. Nothing to set up: it works by itself and keeps progress
/// between scenes (like money and items). Progress resets when you stop Play / quit the game.
///
/// Other scripts can call:
///   QuestManager.StartQuest(quest)        QuestManager.TriggerEvent("QueenNibbler")
///   QuestManager.GetState(quest)          QuestManager.CurrentStep(quest)
public static class QuestManager
{
    public enum State { NotStarted, Active, Completed }

    class Progress { public int step; public bool done; }

    static readonly Dictionary<QuestData, Progress> progress = new Dictionary<QuestData, Progress>();
    static readonly List<QuestData> order = new List<QuestData>();          // in the order they were started
    static readonly HashSet<string> happened = new HashSet<string>();      // events that have happened

    /// Something changed (for the quest log to redraw).
    public static event Action Changed;
    public static event Action<QuestData> QuestStarted;
    public static event Action<QuestData, int> StepCompleted;
    public static event Action<QuestData> QuestCompleted;

    /// Font for the popups (QuestLogUI sets it).
    public static TMP_FontAsset UIFont;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        progress.Clear(); order.Clear(); happened.Clear();
        Changed = null; QuestStarted = null; StepCompleted = null; QuestCompleted = null;
        UIFont = null;
    }

    // ------------------------------------------------------------------ reading

    public static State GetState(QuestData q)
    {
        if (q == null || !progress.TryGetValue(q, out Progress p)) return State.NotStarted;
        return p.done ? State.Completed : State.Active;
    }

    /// Index of the current step (0 = first). Equals steps.Count when the quest is finished.
    public static int CurrentStep(QuestData q)
    {
        return q != null && progress.TryGetValue(q, out Progress p) ? p.step : 0;
    }

    public static QuestStep CurrentStepData(QuestData q)
    {
        if (GetState(q) != State.Active) return null;
        int i = CurrentStep(q);
        return i >= 0 && i < q.steps.Count ? q.steps[i] : null;
    }

    /// Quests that have been started, oldest first.
    public static IReadOnlyList<QuestData> Started => order;

    // ------------------------------------------------------------------ changing

    public static void StartQuest(QuestData q)
    {
        if (q == null || GetState(q) != State.NotStarted) return;
        progress[q] = new Progress();
        order.Add(q);
        Runner.Ensure();
        QuestStarted?.Invoke(q);
        QuestToast.Show("NEW QUEST", q.questName, StepText(q, 0));
        Changed?.Invoke();
        if (q.steps.Count == 0) Complete(q);
        else CheckAutomaticSteps();
    }

    /// Finishes the current step and moves on (finishing the quest after the last step).
    public static void CompleteStep(QuestData q)
    {
        if (GetState(q) != State.Active) return;
        Progress p = progress[q];
        int finished = p.step;
        p.step++;
        StepCompleted?.Invoke(q, finished);
        if (p.step >= q.steps.Count) { Complete(q); return; }
        QuestToast.Show("QUEST UPDATED", q.questName, StepText(q, p.step));
        Changed?.Invoke();
        CheckAutomaticSteps();
    }

    /// Tell quests that something happened (e.g. "QueenNibbler" when the boss is defeated).
    /// It's remembered, so a step waiting for it later still completes.
    public static void TriggerEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return;
        happened.Add(eventId.Trim().ToLowerInvariant());
        CheckAutomaticSteps();
    }

    public static bool HasHappened(string eventId) =>
        !string.IsNullOrEmpty(eventId) && happened.Contains(eventId.Trim().ToLowerInvariant());

    static void Complete(QuestData q)
    {
        Progress p = progress[q];
        p.done = true;
        p.step = q.steps.Count;
        GiveRewards(q);
        QuestCompleted?.Invoke(q);
        QuestToast.Show("QUEST COMPLETE", q.questName, RewardText(q));
        Changed?.Invoke();
    }

    /// Completes CollectItem steps (item in inventory) and Event steps (event already happened).
    public static void CheckAutomaticSteps()
    {
        bool again = true;
        int guard = 0;
        while (again && guard++ < 20)
        {
            again = false;
            foreach (QuestData q in order.ToArray())
            {
                QuestStep s = CurrentStepData(q);
                if (s == null) continue;
                bool ready =
                    (s.type == QuestStepType.CollectItem && s.item != null && CountItem(s.item) >= s.amount) ||
                    (s.type == QuestStepType.Event && HasHappened(s.eventId));
                if (ready) { CompleteStep(q); again = true; }
            }
        }
    }

    // ------------------------------------------------------------------ NPC talk

    public static bool StepIsWith(QuestStep s, string npcId)
    {
        return s != null && (s.type == QuestStepType.TalkTo || s.type == QuestStepType.BringItem)
               && SameId(s.npcId, npcId);
    }

    public static bool SameId(string a, string b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ inventory

    /// How many of an item the player has (all inventory slots).
    public static int CountItem(ItemSO item)
    {
        if (item == null) return 0;
        int n = 0;
        foreach (Slot s in AllSlots())
            if (s.HasItem() && s.GetItem() == item) n += s.GetAmount();
        return n;
    }

    /// Takes items out of the inventory. Returns false (and takes nothing) if there aren't enough.
    public static bool RemoveItem(ItemSO item, int amount)
    {
        if (item == null || amount <= 0) return true;
        if (CountItem(item) < amount) return false;
        int left = amount;
        foreach (Slot s in AllSlots())
        {
            if (left <= 0) break;
            if (!s.HasItem() || s.GetItem() != item) continue;
            int take = Mathf.Min(left, s.GetAmount());
            s.RemoveAmount(take);
            left -= take;
        }
        return true;
    }

    static readonly List<Slot> slotBuffer = new List<Slot>();
    static List<Slot> AllSlots()
    {
        slotBuffer.Clear();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects())
                slotBuffer.AddRange(root.GetComponentsInChildren<Slot>(true));
        }
        return slotBuffer;
    }

    // ------------------------------------------------------------------ rewards / text

    static void GiveRewards(QuestData q)
    {
        // same way NPCInteractable gives rewards
        PlayerStats stats = PlayerStats.Instance;
        Inventory inv = Inventory.Find();
        if (q.moneyReward != 0)
        {
            if (inv != null) inv.AddCurrency(q.moneyReward);
            else if (stats != null) stats.AddMoney(q.moneyReward);
        }
        if (stats != null && q.reputationReward != 0) stats.AddReputation(q.reputationReward);
        if (inv != null)
            foreach (QuestItemReward r in q.itemRewards)
                if (r.item != null && r.amount > 0) inv.AddItem(r.item, r.amount);
    }

    public static string StepText(QuestData q, int i)
    {
        if (q == null || i < 0 || i >= q.steps.Count) return "";
        QuestStep s = q.steps[i];
        if (!string.IsNullOrEmpty(s.logText)) return s.logText;
        switch (s.type)
        {
            case QuestStepType.TalkTo: return "Talk to " + s.npcId;
            case QuestStepType.BringItem: return "Bring " + ItemName(s.item) + " to " + s.npcId;
            case QuestStepType.CollectItem: return "Find " + ItemName(s.item);
            default: return s.eventId;
        }
    }

    static string ItemName(ItemSO item) => item == null ? "the item" : (string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName);

    static string RewardText(QuestData q)
    {
        var parts = new List<string>();
        if (q.moneyReward > 0) parts.Add("+" + q.moneyReward + " coins");
        if (q.reputationReward > 0) parts.Add("+" + q.reputationReward + " rep");
        foreach (QuestItemReward r in q.itemRewards)
            if (r.item != null) parts.Add("+" + r.amount + " " + ItemName(r.item));
        return string.Join("   ", parts);
    }

    // ------------------------------------------------------------------ background check

    /// Checks CollectItem steps a few times a second (items can arrive from anywhere).
    class Runner : MonoBehaviour
    {
        static Runner instance;
        float next;

        public static void Ensure()
        {
            if (instance) return;
            var go = new GameObject("QuestManager");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Runner>();
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.4f;
            CheckAutomaticSteps();
        }
    }
}

/// "NEW QUEST / QUEST UPDATED / QUEST COMPLETE" popup at the top of the screen.
public class QuestToast : MonoBehaviour
{
    static QuestToast instance;
    readonly Queue<string[]> queue = new Queue<string[]>();
    CanvasGroup group;
    TextMeshProUGUI header, title, line;
    float shownAt = -99f;
    const float Duration = 3.2f;

    static readonly Color Gold = new Color(0.96f, 0.78f, 0.42f);
    static readonly Color Cream = new Color(0.98f, 0.95f, 0.88f);
    static readonly Color Hint = new Color(0.80f, 0.76f, 0.70f);

    public static void Show(string head, string questName, string detail)
    {
        if (!instance)
        {
            var go = new GameObject("QuestToast", typeof(RectTransform));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<QuestToast>();
            instance.Build();
        }
        instance.queue.Enqueue(new[] { head, questName, detail });
    }

    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;

        header = Text("Header", 24f, Gold, -150f);
        header.characterSpacing = 10f;
        title = Text("Quest", 40f, Cream, -182f);
        title.characterSpacing = 4f;
        line = Text("Detail", 24f, Hint, -232f);
    }

    // Changing font after an outline was added keeps the old font's material (scrambled letters),
    // so swap the material too and add the outline again.
    static void SetFont(TextMeshProUGUI t, TMP_FontAsset font)
    {
        t.font = font;
        t.fontSharedMaterial = font.material;
        t.outlineWidth = 0.2f;
        t.outlineColor = new Color32(12, 10, 16, 255);
    }

    TextMeshProUGUI Text(string name, float size, Color color, float y)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.sizeDelta = new Vector2(1200f, size * 1.4f);
        r.anchoredPosition = new Vector2(0f, y);
        var t = go.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = GameFont();
        if (f) { t.font = f; t.fontSharedMaterial = f.material; }
        t.fontSize = size; t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.outlineWidth = 0.2f;
        t.outlineColor = new Color32(12, 10, 16, 255);
        return t;
    }

    PlayerInteract interact;
    float nextFind;

    // Popups wait until the conversation is over (they're queued while a dialogue bubble is open).
    bool Talking()
    {
        if (!interact && Time.unscaledTime >= nextFind)
        {
            interact = FindAnyObjectByType<PlayerInteract>();
            nextFind = Time.unscaledTime + 1f;
        }
        return interact && interact.IsInteracting;
    }

    // The game's font: QuestLogUI's Font if set, otherwise the Kleader font the speech bubbles use.
    static TMP_FontAsset GameFont()
    {
        if (QuestManager.UIFont) return QuestManager.UIFont;
        foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (f && f.name.IndexOf("Kleader", System.StringComparison.OrdinalIgnoreCase) >= 0) { QuestManager.UIFont = f; return f; }
        return null;
    }

    void Update()
    {
        float age = Time.unscaledTime - shownAt;
        if (age >= Duration && queue.Count > 0 && !Talking())
        {
            string[] m = queue.Dequeue();
            TMP_FontAsset font = GameFont();
            if (font && header.font != font) { SetFont(header, font); SetFont(title, font); SetFont(line, font); }
            header.text = m[0]; title.text = m[1]; line.text = m[2];
            shownAt = Time.unscaledTime;
            age = 0f;
        }
        float a = age < 0.25f ? age / 0.25f : age > Duration - 0.5f ? Mathf.Clamp01((Duration - age) / 0.5f) : 1f;
        group.alpha = a;
        float pop = age < 0.25f ? Mathf.Lerp(1.15f, 1f, age / 0.25f) : 1f;
        title.transform.localScale = Vector3.one * pop;
    }
}