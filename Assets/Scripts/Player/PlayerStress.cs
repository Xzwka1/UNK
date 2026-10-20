using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public enum StressStage
{
    Critical_Brittle,   // -80% to -99.9% (Critical Crystallization)
    Strained_Brittle,   // -50% to -79.9%
    Stable,             // -49.9% to +49.9%
    Strained_Tensile,   // +50% to +79.9%
    Critical_Tensile,   // +80% to +99.9% (Critical Meltdown)

    // Legacy aliases for backward compatibility
    Stage1_Stable = Stable,
    Stage2_Strained = Strained_Tensile,
    Stage3_Critical = Critical_Tensile
}

/// <summary>
/// The Fragile Equilibrium: Dual-Polarity Stress System (-100% ถึง +100%).
///  - ขั้วบวก (+1% ถึง +100%): สภาวะเหลว/ฉีกยืด (Tensile / Liquid State)
///  - ขั้วลบ (-1% ถึง -100%): สภาวะแข็งกรอบเป็นผลึกแก้ว (Brittle / Crystal State)
///  - จุดสมดุล: 0%
/// รวมทั้งระบบ Polarity Snap Bonus, Visual Tinting, และ Dual Overload Death
/// </summary>
public class PlayerStress : MonoBehaviour
{
    // ------------------------------------------------------------------ Inspector: Thresholds & Recovery
    [Header("Dual-Polarity Thresholds (%)")]
    [SerializeField] private float minStress = -100f;
    [SerializeField] private float maxStress = 100f;
    [Tooltip("Threshold where Strained state begins (±50%) and Polarity Snap becomes armed.")]
    [SerializeField] private float strainedThreshold = 50f;
    [Tooltip("Threshold where Critical Lockouts begin (±80%).")]
    [SerializeField] private float criticalThreshold = 80f;

    [Header("Safe Ground Recovery")]
    [Tooltip("Rate at which currentStress moves toward 0f when standing on safe ground (10f / sec).")]
    [SerializeField] private float safeGroundRecoveryRate = 10f;

    // ------------------------------------------------------------------ Inspector: Action Modifiers (+)
    [Header("Positive Modifiers (+ Tensile / Liquid)")]
    [Tooltip("Normal 8-Direction Dash stress cost (+25f).")]
    [SerializeField] private float normalDashStress = 25f;
    [Tooltip("Thermal Shock Dash cost when dashing while currentStress < 0f (+35f).")]
    [SerializeField] private float thermalShockDashStress = 35f;
    [Tooltip("Emergency 2nd Air Dash stress cost (+45f).")]
    [SerializeField] private float emergencyAirDashStress = 45f;
    [Tooltip("Double-Jump (Air Jump) stress cost (+20f).")]
    [SerializeField] private float doubleJumpStress = 20f;
    [Tooltip("Wall-Climb Up continuous stress rate per second (+30f/s).")]
    [SerializeField] private float wallClimbUpStressRate = 30f;

    // ------------------------------------------------------------------ Inspector: Action Modifiers (-)
    [Header("Negative Modifiers (- Brittle / Crystal)")]
    [Tooltip("Wall Hold Still continuous stress rate per second (-25f/s).")]
    [SerializeField] private float wallHoldStillStressRate = -25f;
    [Tooltip("Wall Slide Down continuous stress rate per second (-35f/s).")]
    [SerializeField] private float wallSlideDownStressRate = -35f;
    [Tooltip("Wall-Jump instant stress change (-20f).")]
    [SerializeField] private float wallJumpStress = -20f;
    [Tooltip("Smashing through a BreakingBarrier instant stress change (-30f).")]
    [SerializeField] private float breakingBarrierStress = -30f;
    [Tooltip("Heavy Landing instant stress change (-15f).")]
    [SerializeField] private float heavyLandingStress = -15f;

    // ------------------------------------------------------------------ Inspector: Visuals & Death Setup
    [Header("Visual Hooks & Overlays")]
    [Tooltip("Main character SpriteRenderer (used for Polarity Snap white flash and stress tint). Auto-found if empty.")]
    [SerializeField] private SpriteRenderer playerSpriteRenderer;
    [Tooltip("Duration of the white flash when Polarity Snap triggers (0.1s).")]
    [SerializeField] private float polaritySnapFlashDuration = 0.1f;

    [Tooltip("Optional overlay SpriteRenderer (kept disabled during locomotion so it doesn't double-render over animations).")]
    [SerializeField] private SpriteRenderer crystalCrackOverlay;
    [Tooltip("Sprite shown when Crystal Shatter Overload (-100%) freezes the character solid.")]
    [SerializeField] private Sprite brittleShatterFrameSprite;
    [Tooltip("ParticleSystem played on Crystal Shatter Overload (-100%).")]
    [SerializeField] private ParticleSystem crystalShatterParticles;

    [Header("Runtime State")]
    [SerializeField, Range(-100f, 100f)] private float currentStress;

    // ------------------------------------------------------------------ Inspector: UnityEvents (UI & SFX Hooks)
    [Header("UI & Action Events")]
    [Tooltip("Passes currentStress (-100 to +100) whenever it changes.")]
    public UnityEvent<float> OnStressChangedInstance;
    [Tooltip("Fired when dashing while currentStress < 0f (+35f Thermal Shock). Play steam 'Shhh!' SFX here.")]
    public UnityEvent OnThermalShock;
    [Tooltip("Fired when a change, armed by the 50% Strained zone, carries the value across 0f to the opposite pole. Play chime 'Ping!' SFX here.")]
    public UnityEvent OnPolaritySnap;

    [Header("Hard Lockout Events (±80%)")]
    [Tooltip("Fired when pressing Dash or Double-Jump at >= +80f stress. Show flat puddle frame & play splash SFX.")]
    public UnityEvent OnLiquidLockoutFail;
    [Tooltip("Fired when touching a wall at <= -80f stress. Show crystal crack frame & play stone scrape SFX.")]
    public UnityEvent OnBrittleWallSlip;

    [Header("Dual Overload Death Events (±100%)")]
    [Tooltip("Fired at +100% Stress (Meltdown Overload). Trigger slime explosion animation & SFX.")]
    public UnityEvent OnMeltdownOverload;
    [Tooltip("Fired at -100% Stress (Crystal Shatter Overload). Trigger solid crystal death animation & glass shatter SFX.")]
    public UnityEvent OnCrystalShatterOverload;

    [Header("Stage & Impact Events")]
    public UnityEvent OnEnterStable;
    public UnityEvent OnEnterStrainedTensile;
    public UnityEvent OnEnterCriticalTensile;
    public UnityEvent OnEnterStrainedBrittle;
    public UnityEvent OnEnterCriticalBrittle;
    public UnityEvent OnHardLandingImpact;

    // ------------------------------------------------------------------ C# Events
    /// <summary>Static event passing SignedNormalized (-1..+1) for HUD UI Controllers.</summary>
    public static event Action<float> OnStressChanged;
    /// <summary>Fired once when stress hits -100% or +100%.</summary>
    public event Action Overloaded;
    /// <summary>Fired when Polarity Snap triggers so PlayerController2D can refill dash & air jumps.</summary>
    public event Action PolaritySnapped;
    public event Action<StressStage> StageChanged;

    // ------------------------------------------------------------------ Private Visual Cache
    private Sprite baseSprite;
    private Color baseSpriteColor = Color.white;
    private Coroutine flashRoutine;
    private StressStage lastStage = StressStage.Stable;

    /// <summary>Armed once the Strained zone (plus/minus 50%) is reached; the next single change that crosses 0% fires the snap.</summary>
    private bool polaritySnapArmed;

    // ------------------------------------------------------------------ Public Read-Only Properties
    public float Current => currentStress;
    public float CurrentStress => currentStress;
    /// <summary>Normalized value in [-1..+1] for dual-polarity UI bars.</summary>
    public float SignedNormalized => Mathf.Clamp(currentStress / 100f, -1f, 1f);
    /// <summary>Magnitude in [0..1].</summary>
    public float Normalized => Mathf.Clamp01(Mathf.Abs(currentStress) / 100f);

    public bool IsOverloaded { get; private set; }
    public bool IsLiquidLocked => currentStress >= criticalThreshold;
    public bool IsBrittleLocked => currentStress <= -criticalThreshold;

    public float StrainedThreshold => strainedThreshold;
    public float CriticalThreshold => criticalThreshold;

    public float NormalDashStress => normalDashStress;
    public float ThermalShockDashStress => thermalShockDashStress;
    public float EmergencyAirDashStress => emergencyAirDashStress;
    public float DoubleJumpStress => doubleJumpStress;
    public float WallClimbUpStressRate => wallClimbUpStressRate;
    public float WallHoldStillStressRate => wallHoldStillStressRate;
    public float WallSlideDownStressRate => wallSlideDownStressRate;
    public float WallJumpStress => wallJumpStress;
    public float BreakingBarrierStress => breakingBarrierStress;
    public float HeavyLandingStress => heavyLandingStress;

    public StressStage Stage => StageFor(currentStress);

    // ------------------------------------------------------------------ Lifecycle
    private void Awake()
    {
        if (playerSpriteRenderer == null)
        {
            Transform vis = transform.Find("Visual");
            if (vis != null) playerSpriteRenderer = vis.GetComponent<SpriteRenderer>();
            if (playerSpriteRenderer == null) playerSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (playerSpriteRenderer != null)
        {
            baseSprite = playerSpriteRenderer.sprite;
            baseSpriteColor = playerSpriteRenderer.color;
        }

        lastStage = Stage;
    }

    public void ConfigureVisuals(SpriteRenderer sr, SpriteRenderer overlay, Sprite shatterSprite)
    {
        playerSpriteRenderer = sr;
        crystalCrackOverlay = overlay;
        brittleShatterFrameSprite = shatterSprite;
        if (sr != null && baseSprite == null)
        {
            baseSprite = sr.sprite;
            baseSpriteColor = sr.color;
        }
        UpdateCrystalCrackOverlay();
    }

    // ------------------------------------------------------------------ Core Modification & Polarity Snap
    public void ModifyStress(float amount, bool isInstant = true)
    {
        if (IsOverloaded) return;

        float previousStress = currentStress;
        currentStress = Mathf.Clamp(currentStress + amount, minStress, maxStress);

        // Polarity Snap Bonus (armed-crossing rule):
        // Reaching the Strained zone (|x| >= 50%) arms the snap; the first single change that carries
        // the value across 0f to the opposite pole fires it (refunds 1 Air Dash + Double-Jump mid-air).
        if (polaritySnapArmed
            && ((previousStress > 0f && currentStress < 0f) || (previousStress < 0f && currentStress > 0f)))
        {
            polaritySnapArmed = false;
            TriggerPolaritySnap();
        }

        ApplyPostChangeUpdates();
    }

    /// <summary>Recovers towards 0% while standing on safe ground.</summary>
    public void RecoverOnSafeGround(float dt)
    {
        if (IsOverloaded || Mathf.Approximately(currentStress, 0f)) return;

        currentStress = Mathf.MoveTowards(currentStress, 0f, safeGroundRecoveryRate * dt);
        ApplyPostChangeUpdates();
    }

    // ------------------------------------------------------------------ Action Methods (called by PlayerController2D)
    public void ApplyNormalDashStress()
    {
        if (currentStress < 0f)
        {
            ModifyStress(thermalShockDashStress, true);
            OnThermalShock?.Invoke();
        }
        else
        {
            ModifyStress(normalDashStress, true);
        }
    }

    public void ApplyEmergencyAirDashStress() => ModifyStress(emergencyAirDashStress, true);
    public void ApplyDoubleJumpStress() => ModifyStress(doubleJumpStress, true);
    public void ApplyWallClimbStress(float dt) => ModifyStress(wallClimbUpStressRate * dt, false);
    public void ApplyWallHoldStress(float dt) => ModifyStress(wallHoldStillStressRate * dt, false);
    public void ApplyWallSlideStress(float dt) => ModifyStress(wallSlideDownStressRate * dt, false);
    public void ApplyWallJumpStress() => ModifyStress(wallJumpStress, true);
    public void ApplyBreakingBarrierStress() => ModifyStress(breakingBarrierStress, true);

    public void ApplyHeavyLandingStress()
    {
        OnHardLandingImpact?.Invoke();
        if (IsBrittleLocked)
        {
            ModifyStress(-100f, true);
        }
        else
        {
            ModifyStress(heavyLandingStress, true);
        }
    }

    // Legacy compatibility wrappers
    public void Add(float amount, bool isInstant = true) => ModifyStress(amount, isInstant);
    public void AddRate(float perSecond, float dt) => ModifyStress(perSecond * dt, false);
    public void Recover(float dt) => RecoverOnSafeGround(dt);

    // ------------------------------------------------------------------ Lockout Feedback Triggers
    public void NotifyLiquidLockoutFail() => OnLiquidLockoutFail?.Invoke();
    public void NotifyBrittleWallSlip() => OnBrittleWallSlip?.Invoke();
    public void NotifyHardLanding() => OnHardLandingImpact?.Invoke();

    // ------------------------------------------------------------------ Polarity Snap Bonus
    private void TriggerPolaritySnap()
    {
        PolaritySnapped?.Invoke();
        OnPolaritySnap?.Invoke();

        if (playerSpriteRenderer != null && gameObject.activeInHierarchy)
        {
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(PolaritySnapFlashRoutine());
        }
    }

    private IEnumerator PolaritySnapFlashRoutine()
    {
        playerSpriteRenderer.color = Color.white;
        yield return new WaitForSeconds(polaritySnapFlashDuration);
        flashRoutine = null;
        if (!IsOverloaded && playerSpriteRenderer != null)
            UpdateCrystalCrackOverlay();
    }

    // ------------------------------------------------------------------ Post-Change, Overlays & Dual Overload Death
    private void ApplyPostChangeUpdates()
    {
        UpdateCrystalCrackOverlay();

        // Arming: reaching the Strained zone (|x| >= 50%) arms the next crossing of 0%.
        if (Mathf.Abs(currentStress) >= strainedThreshold)
            polaritySnapArmed = true;

        EvaluateStage();
        OnStressChangedInstance?.Invoke(currentStress);
        OnStressChanged?.Invoke(SignedNormalized);

        if (currentStress >= maxStress)
        {
            TriggerMeltdownOverload();
        }
        else if (currentStress <= minStress)
        {
            TriggerCrystalShatterOverload();
        }
    }

    /// <summary>Updates the visual tint based on dual-polarity stress.</summary>
    private void UpdateCrystalCrackOverlay()
    {
        if (playerSpriteRenderer != null && flashRoutine == null && !IsOverloaded)
        {
            if (currentStress <= -strainedThreshold)
            {
                float t = Mathf.Clamp01((Mathf.Abs(currentStress) - strainedThreshold) / Mathf.Max(1f, Mathf.Abs(minStress) - strainedThreshold));
                playerSpriteRenderer.color = Color.Lerp(baseSpriteColor, new Color(0.55f, 0.92f, 1f, 1f), t * 0.65f);
            }
            else if (currentStress >= strainedThreshold)
            {
                float t = Mathf.Clamp01((currentStress - strainedThreshold) / Mathf.Max(1f, maxStress - strainedThreshold));
                playerSpriteRenderer.color = Color.Lerp(baseSpriteColor, new Color(1f, 0.55f, 0.45f, 1f), t * 0.6f);
            }
            else
            {
                playerSpriteRenderer.color = baseSpriteColor;
            }
        }

        if (crystalCrackOverlay != null)
        {
            crystalCrackOverlay.enabled = false;
        }
    }

    /// <summary>Positive Overload (+100%): Meltdown death.</summary>
    public void TriggerMeltdownOverload()
    {
        if (IsOverloaded) return;
        IsOverloaded = true;

        if (playerSpriteRenderer != null)
            playerSpriteRenderer.color = baseSpriteColor;

        OnMeltdownOverload?.Invoke();
        Overloaded?.Invoke();
    }

    /// <summary>Negative Overload (-100%): Crystal Shatter death.</summary>
    public void TriggerCrystalShatterOverload()
    {
        if (IsOverloaded) return;
        IsOverloaded = true;

        if (playerSpriteRenderer != null)
            playerSpriteRenderer.color = baseSpriteColor;

        OnCrystalShatterOverload?.Invoke();
        if (gameObject.activeInHierarchy)
            StartCoroutine(CrystalShatterDeathRoutine());
        Overloaded?.Invoke();
    }

    private IEnumerator CrystalShatterDeathRoutine()
    {
        if (playerSpriteRenderer != null)
        {
            playerSpriteRenderer.enabled = true;
            if (brittleShatterFrameSprite != null)
                playerSpriteRenderer.sprite = brittleShatterFrameSprite;
        }

        if (crystalCrackOverlay != null)
            crystalCrackOverlay.enabled = false;

        if (crystalShatterParticles != null)
            crystalShatterParticles.Play();

        yield return null;
    }

    // ------------------------------------------------------------------ Reset on Respawn
    public void ResetStress()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        currentStress = 0f;
        IsOverloaded = false;
        polaritySnapArmed = false;

        if (playerSpriteRenderer != null)
        {
            playerSpriteRenderer.enabled = true;
            playerSpriteRenderer.color = baseSpriteColor;
            if (baseSprite != null) playerSpriteRenderer.sprite = baseSprite;
        }

        UpdateCrystalCrackOverlay();
        EvaluateStage();
        OnStressChangedInstance?.Invoke(currentStress);
        OnStressChanged?.Invoke(0f);
    }

    // ------------------------------------------------------------------ Stage Evaluation
    public StressStage StageFor(float value)
    {
        if (value >= criticalThreshold) return StressStage.Critical_Tensile;
        if (value >= strainedThreshold) return StressStage.Strained_Tensile;
        if (value <= -criticalThreshold) return StressStage.Critical_Brittle;
        if (value <= -strainedThreshold) return StressStage.Strained_Brittle;
        return StressStage.Stable;
    }

    public float StageThreshold(StressStage s)
    {
        switch (s)
        {
            case StressStage.Critical_Tensile:
            case StressStage.Critical_Brittle:
                return criticalThreshold;
            case StressStage.Strained_Tensile:
            case StressStage.Strained_Brittle:
                return strainedThreshold;
            default:
                return 0f;
        }
    }

    private void EvaluateStage()
    {
        StressStage s = Stage;
        if (s == lastStage) return;
        lastStage = s;
        StageChanged?.Invoke(s);

        switch (s)
        {
            case StressStage.Stable: OnEnterStable?.Invoke(); break;
            case StressStage.Strained_Tensile: OnEnterStrainedTensile?.Invoke(); break;
            case StressStage.Critical_Tensile: OnEnterCriticalTensile?.Invoke(); break;
            case StressStage.Strained_Brittle: OnEnterStrainedBrittle?.Invoke(); break;
            case StressStage.Critical_Brittle: OnEnterCriticalBrittle?.Invoke(); break;
        }
    }
}
