using System;
using UnityEngine;

/// <summary>
/// Stress / body-overload meter (0..100 %). Holds only the value and the rules for changing it;
/// PlayerController2D decides WHEN each rate applies. Raises <see cref="Overloaded"/> once when 100 % is reached.
/// </summary>
public class PlayerStress : MonoBehaviour
{
    [Header("Continuous rates (% per second)")]
    public float gripHoldRate = 20f;        // gripping a wall without moving
    public float climbUpRate = 35f;         // climbing up
    public float slideDownRate = 0f;        // sliding down
    public float groundRecoveryRate = 40f;  // recovery while on safe ground

    [Header("Instant costs (%)")]
    public float wallJumpCost = 20f;

    [Header("Runtime value")]
    [SerializeField, Range(0f, 100f)] private float current;

    /// <summary>Current stress in percent (0..100).</summary>
    public float Current => current;
    /// <summary>Current stress as 0..1 (handy for a future UI).</summary>
    public float Normalized => current / 100f;
    public bool IsOverloaded { get; private set; }

    /// <summary>Fired once when stress reaches 100 %.</summary>
    public event Action Overloaded;

    /// <summary>Adds (or removes, if negative) a flat amount of stress in percent.</summary>
    public void Add(float amount)
    {
        if (IsOverloaded) return;
        current = Mathf.Clamp(current + amount, 0f, 100f);
        if (current >= 100f)
        {
            IsOverloaded = true;
            Overloaded?.Invoke();
        }
    }

    /// <summary>Adds stress at a rate in percent/second over one time step.</summary>
    public void AddRate(float perSecond, float dt) => Add(perSecond * dt);

    /// <summary>Safe-ground recovery over one time step.</summary>
    public void Recover(float dt)
    {
        if (IsOverloaded) return;
        current = Mathf.Max(0f, current - groundRecoveryRate * dt);
    }

    public void ResetStress()
    {
        current = 0f;
        IsOverloaded = false;
    }
}
