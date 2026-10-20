using UnityEngine;

/// <summary>
/// Marker component. Any trigger collider carrying this component kills the player
/// when it touches the player's Hurtbox (pits, spikes, kill zones...).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Hazard2D : MonoBehaviour
{
    [Tooltip("Optional label, useful for debugging.")]
    public string hazardName = "Hazard";

    private void Reset()
    {
        // Hazards are detected by the Hurtbox trigger, so they should be triggers themselves.
        var c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }
}
