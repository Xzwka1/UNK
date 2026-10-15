using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A solid barrier the player can smash by dashing into it. A dash that breaks it takes no wall-impact stress
/// and counts as a safe "end" for the Last Stand Dash (see PlayerController2D.OnDashEnd).
/// </summary>
public class BreakingBarrier : MonoBehaviour
{
    [Tooltip("Hook for particles / audio when the barrier breaks.")]
    public UnityEvent OnBroken;

    [Tooltip("Destroy the GameObject when broken. If off, it is only deactivated.")]
    [SerializeField] private bool destroyOnBreak = true;

    public void Break()
    {
        OnBroken?.Invoke();
        if (destroyOnBreak) Destroy(gameObject);
        else gameObject.SetActive(false);
    }
}
