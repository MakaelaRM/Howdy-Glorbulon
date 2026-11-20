using UnityEngine;

/// Sends a quest event, so an "Event" quest step can complete.
/// Use it from any UnityEvent (a button, a trigger, a boss's On Defeated...): drag this object in and pick
/// QuestEventTrigger > Trigger. Or tick "On Player Enter" to fire it when the player walks into this
/// object's trigger collider (e.g. "reach the mine entrance").
public class QuestEventTrigger : MonoBehaviour
{
    [Tooltip("Must match the Event Id on the quest step.")]
    public string eventId = "ReachedTheMine";
    [Tooltip("Fire when the player walks into this object's collider (tick Is Trigger on the collider).")]
    public bool onPlayerEnter = false;

    public void Trigger() { QuestManager.TriggerEvent(eventId); }

    public void Trigger(string id) { QuestManager.TriggerEvent(id); }

    void OnTriggerEnter(Collider other)
    {
        if (onPlayerEnter && other.CompareTag("Player")) Trigger();
    }
}
