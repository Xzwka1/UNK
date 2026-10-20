using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A solid barrier the player can smash by dashing into it.
/// When broken during a Dash:
///   1. Destroys/deactivates the barrier and fires OnBroken.
///   2. Reduces Stress immediately by -30f (via PlayerStress.ApplyBreakingBarrierStress).
///   3. Refunds 1 Air Dash (canDash = true) immediately to the player.
/// </summary>
public class BreakingBarrier : MonoBehaviour
{
    [Tooltip("Hook for particles / audio when the barrier breaks.")]
    public UnityEvent OnBroken;

    [Tooltip("Destroy the GameObject when broken. If off, it is only deactivated.")]
    [SerializeField] private bool destroyOnBreak = true;

    private bool isBroken;

    /// <summary>Breaks the barrier and applies the -30f stress relief + Air Dash refund to the dashing player.</summary>
    public void Break(PlayerController2D player = null)
    {
        if (isBroken) return;
        isBroken = true;

        if (player != null)
        {
            player.RefundAirDash();
            if (player.TryGetComponent(out PlayerStress stress))
            {
                stress.ApplyBreakingBarrierStress(); // -30f instant
            }
        }

        OnBroken?.Invoke();
        if (destroyOnBreak) Destroy(gameObject);
        else gameObject.SetActive(false);
    }
}
