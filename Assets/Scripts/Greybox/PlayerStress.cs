using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>The three physical states of the slime body. Body Overload (100 %) is handled separately as death.</summary>
public enum StressStage
{
    Stage1_Stable,    // 0   - 49.9 %
    Stage2_Strained,  // 50  - 79.9 %
    Stage3_Critical   // 80  - 99.9 %
}

/// <summary>
/// Every number the designer can tune for ONE stress stage. Edit them in the Inspector on PlayerStress.
/// Percent values are "% of the 0-100 stress bar"; multipliers are plain factors (1 = unchanged).
/// </summary>
[Serializable]
public struct StressStageParams
{
    [Header("Movement physics")]
    [Tooltip("Seconds from max speed to a full stop on the ground (higher = more slippery).")]
    public float groundDecelTime;
    [Tooltip("Seconds from 0 to max speed when steering left/right in mid-air (higher = more inertia).")]
    public float airAccelTime;
    [Tooltip("Downward speed (units/s, positive number) while gripping a wall WITHOUT pressing W/S. 0 = sticks still.")]
    public float wallHoldSlipSpeed;
    [Tooltip("Stress gained per second while gripping a wall without pressing W/S.")]
    public float wallHoldStressRate;

    [Header("Wall climb (Ctrl + W)")]
    [Tooltip("Fraction of the base climb speed. 0 = climbing disabled (the player slips instead).")]
    public float climbSpeedMultiplier;
    [Tooltip("Stress gained per second while climbing up.")]
    public float climbStressRate;

    [Header("Wall slide (Ctrl + S)")]
    [Tooltip("Stress change per second while sliding down. NEGATIVE = active recovery (compresses the slime).")]
    public float wallSlideStressRate;

    [Header("Air actions")]
    [Tooltip("Multiplier on dash speed (and therefore distance, as the duration is fixed).")]
    public float dashMultiplier;
    [Tooltip("Multiplier on air-jump height.")]
    public float airJumpHeightMultiplier;
    [Tooltip("Stress added immediately by each Dash or Double-Jump.")]
    public float dashAirJumpCost;

    [Header("Impact fragility")]
    [Tooltip("Stress added when landing faster than the heavy-landing velocity threshold.")]
    public float heavyLandingCost;
    [Tooltip("Stress added when dashing into a solid wall (anything that is not a BreakingBarrier).")]
    public float solidWallDashCost;

    // ---- Designer baselines (the values from the design document) ----
    public static StressStageParams Stable => new StressStageParams
    {
        groundDecelTime = 0.02f, airAccelTime = 0.04f,
        wallHoldSlipSpeed = 0f, wallHoldStressRate = 20f,
        climbSpeedMultiplier = 1f, climbStressRate = 35f,
        wallSlideStressRate = 0f,
        dashMultiplier = 1f, airJumpHeightMultiplier = 1f, dashAirJumpCost = 0f,
        heavyLandingCost = 0f, solidWallDashCost = 0f
    };

    public static StressStageParams Strained => new StressStageParams
    {
        groundDecelTime = 0.12f, airAccelTime = 0.08f,
        wallHoldSlipSpeed = 1.5f, wallHoldStressRate = 20f,
        climbSpeedMultiplier = 0.8f, climbStressRate = 35f,
        wallSlideStressRate = -15f,
        dashMultiplier = 1.10f, airJumpHeightMultiplier = 1f, dashAirJumpCost = 10f,
        heavyLandingCost = 10f, solidWallDashCost = 10f
    };

    public static StressStageParams Critical => new StressStageParams
    {
        groundDecelTime = 0.22f, airAccelTime = 0.12f,
        wallHoldSlipSpeed = 4f, wallHoldStressRate = 20f,
        climbSpeedMultiplier = 0f, climbStressRate = 35f,
        wallSlideStressRate = -15f,
        dashMultiplier = 1.25f, airJumpHeightMultiplier = 0.75f, dashAirJumpCost = 15f,
        heavyLandingCost = 20f, solidWallDashCost = 20f
    };
}

/// <summary>
/// Stress / body-overload meter (0..100 %) and the 3-stage fragility model built on top of it.
///
/// STAGE TRANSITIONS (evaluated live from the current value):
///   0 - 49.9 % -> Stage1_Stable   |   50 - 79.9 % -> Stage2_Strained   |   80 - 99.9 % -> Stage3_Critical
///   100 %      -> Body Overload (death), EXCEPT during the "Last Stand Dash" (see below).
///
/// LAST STAND DASH: PlayerController2D calls <see cref="BeginOverloadDefer"/> when a dash starts. While deferred,
/// reaching 100 % does NOT fire <see cref="Overloaded"/>; the controller resolves it when the dash ends via
/// <see cref="ResolveOverloadDefer"/> (survive -> clamp to 95 %, otherwise die). Hard impacts bypass the deferral.
///
/// This class owns only the value, the stage lookup and the events; PlayerController2D decides WHEN costs apply.
/// </summary>
public class PlayerStress : MonoBehaviour
{
    [Header("Stage thresholds (% stress at which a stage begins)")]
    [SerializeField] private float stage2Threshold = 50f;
    [SerializeField] private float stage3Threshold = 80f;

    [Header("Per-stage parameters")]
    [SerializeField] private StressStageParams stage1 = StressStageParams.Stable;
    [SerializeField] private StressStageParams stage2 = StressStageParams.Strained;
    [SerializeField] private StressStageParams stage3 = StressStageParams.Critical;

    [Header("Shared rules")]
    [Tooltip("Recovery per second while standing on safe (non-FragilePlatform) ground.")]
    public float groundRecoveryRate = 40f;
    [Tooltip("Instant cost of a wall-jump (max count is set on the controller).")]
    public float wallJumpCost = 20f;
    [Tooltip("Stress the player is clamped to when a Last Stand Dash is survived.")]
    [SerializeField, Range(0f, 99.9f)] private float lastStandSurvivalStress = 95f;

    [Header("Runtime value")]
    [SerializeField, Range(0f, 100f)] private float current;

    [Header("Feedback hooks (tint sprite, crack animation, audio, shake...)")]
    public UnityEvent OnEnterStage1;
    public UnityEvent OnEnterStage2;
    public UnityEvent OnEnterStage3;
    [Tooltip("Fired on every heavy landing (terminal-velocity impact), whatever the stage.")]
    public UnityEvent OnHardLandingImpact;
    [Tooltip("Fired when a Last Stand Dash is survived and stress is clamped to 95 %.")]
    public UnityEvent OnLastStandSaved;
    [Tooltip("Fired (rate-limited by the controller) when the player tries to climb in Stage 3. Hook a subtle shake / fail sound here.")]
    public UnityEvent OnClimbBlocked;

    /// <summary>Fired once when stress reaches 100 % (and is not deferred).</summary>
    public event Action Overloaded;
    /// <summary>Fired whenever the stage changes (new stage as argument).</summary>
    public event Action<StressStage> StageChanged;

    private StressStage lastStage = StressStage.Stage1_Stable;
    private bool deferActive, overloadPending;

    // ------------------------------------------------------------------ Read-only info
    /// <summary>Current stress in percent (0..100).</summary>
    public float Current => current;
    /// <summary>Current stress as 0..1 (handy for a future UI).</summary>
    public float Normalized => current / 100f;
    public bool IsOverloaded { get; private set; }
    /// <summary>The stage derived from the current stress value.</summary>
    public StressStage Stage => StageFor(current);
    /// <summary>Parameters of the stage the player is in right now.</summary>
    public StressStageParams Params => ParamsFor(Stage);

    /// <summary>Stress percent at which the given stage begins (Stage 1 = 0).</summary>
    public float StageThreshold(StressStage s) =>
        s == StressStage.Stage3_Critical ? stage3Threshold :
        s == StressStage.Stage2_Strained ? stage2Threshold : 0f;

    public StressStage StageFor(float value)
    {
        if (value >= stage3Threshold) return StressStage.Stage3_Critical;
        if (value >= stage2Threshold) return StressStage.Stage2_Strained;
        return StressStage.Stage1_Stable;
    }

    public StressStageParams ParamsFor(StressStage s)
    {
        switch (s)
        {
            case StressStage.Stage3_Critical: return stage3;
            case StressStage.Stage2_Strained: return stage2;
            default: return stage1;
        }
    }

    // ------------------------------------------------------------------ Changing stress
    /// <summary>
    /// Adds (or removes, if negative) a flat amount of stress in percent.
    /// <paramref name="forceImmediate"/> = true ignores the Last Stand deferral (used for wall-puncture impacts).
    /// </summary>
    public void Add(float amount, bool forceImmediate = false)
    {
        if (IsOverloaded || Mathf.Approximately(amount, 0f)) return;
        SetCurrent(current + amount, forceImmediate);
    }

    /// <summary>Adds stress at a rate in percent/second over one time step (negative rate = recovery).</summary>
    public void AddRate(float perSecond, float dt) => Add(perSecond * dt);

    /// <summary>Safe-ground recovery over one time step.</summary>
    public void Recover(float dt)
    {
        if (IsOverloaded || current <= 0f) return;
        SetCurrent(current - groundRecoveryRate * dt, false);
    }

    public void ResetStress()
    {
        current = 0f;
        IsOverloaded = false;
        deferActive = overloadPending = false;
        EvaluateStage();
    }

    private void SetCurrent(float value, bool forceImmediate)
    {
        current = Mathf.Clamp(value, 0f, 100f);
        EvaluateStage();

        if (current >= 100f)
        {
            if (deferActive && !forceImmediate) overloadPending = true; // Last Stand Dash: decide when the dash ends
            else FireOverload();
        }
    }

    private void FireOverload()
    {
        if (IsOverloaded) return;
        IsOverloaded = true;
        overloadPending = false;
        deferActive = false;
        Overloaded?.Invoke();
    }

    // ------------------------------------------------------------------ Last Stand Dash
    /// <summary>Starts postponing the 100 % death check (called when a dash begins).</summary>
    public void BeginOverloadDefer() => deferActive = true;

    /// <summary>
    /// Ends the deferral (called when the dash ends). If 100 % was reached meanwhile:
    /// <paramref name="survive"/> true -> clamp to the survival value and raise OnLastStandSaved, false -> die now.
    /// </summary>
    public void ResolveOverloadDefer(bool survive)
    {
        deferActive = false;
        if (!overloadPending || IsOverloaded) return;
        overloadPending = false;

        if (survive)
        {
            SetCurrent(lastStandSurvivalStress, true);
            OnLastStandSaved?.Invoke();
        }
        else
        {
            FireOverload();
        }
    }

    // ------------------------------------------------------------------ Hooks
    public void NotifyHardLanding() => OnHardLandingImpact?.Invoke();
    public void NotifyClimbBlocked() => OnClimbBlocked?.Invoke();

    private void EvaluateStage()
    {
        StressStage s = Stage;
        if (s == lastStage) return;
        lastStage = s;
        StageChanged?.Invoke(s);

        switch (s)
        {
            case StressStage.Stage1_Stable: OnEnterStage1?.Invoke(); break;
            case StressStage.Stage2_Strained: OnEnterStage2?.Invoke(); break;
            case StressStage.Stage3_Critical: OnEnterStage3?.Invoke(); break;
        }
    }
}
