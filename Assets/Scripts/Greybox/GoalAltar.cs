using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Delivery goal. Put on a trigger collider; fires <see cref="OnGoalReached"/> once when the player touches it.
/// (Hook UI / audio / scene loading to the event later.)
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class GoalAltar : MonoBehaviour
{
    [Tooltip("Fired once when the player reaches the altar.")]
    public UnityEvent OnGoalReached;

    public bool Reached { get; private set; }

    private void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Reached) return;
        if (other.GetComponentInParent<PlayerController2D>() == null) return;

        Reached = true;
        Debug.Log("[GoalAltar] Goal reached!");
        OnGoalReached?.Invoke();
    }
}
