using UnityEngine;

/// <summary>
/// Connects player gameplay & stress events to the central SoundManager.
/// Listens to PlayerStress, PlayerRespawn, and PlayerController2D.
/// </summary>
[RequireComponent(typeof(PlayerController2D), typeof(PlayerStress), typeof(PlayerRespawn))]
public class PlayerAudioBridge : MonoBehaviour
{
    [Header("Footsteps")]
    [SerializeField] private float footstepInterval = 0.32f;
    [SerializeField] private float minMoveSpeedForFootstep = 1.2f;

    private PlayerController2D controller;
    private PlayerStress stress;
    private PlayerRespawn respawn;
    private Rigidbody2D rb;

    private float footstepTimer;
    private bool wasWallSliding;

    private void Awake()
    {
        controller = GetComponent<PlayerController2D>();
        stress = GetComponent<PlayerStress>();
        respawn = GetComponent<PlayerRespawn>();
        rb = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        if (stress != null)
        {
            stress.PolaritySnapped += OnPolaritySnap;
            stress.LockoutDenied += OnLockoutDenied;
            stress.OnMeltdownOverload.AddListener(OnMeltdownOverload);
            stress.OnCrystalShatterOverload.AddListener(OnCrystalShatterOverload);
            stress.OnEnterStrainedBrittle.AddListener(OnEnterStrainedBrittle);
            stress.OnEnterCriticalBrittle.AddListener(OnEnterCriticalBrittle);
        }

        if (respawn != null)
        {
            respawn.Died += OnPlayerDied;
            respawn.Respawned += OnPlayerRespawned;
        }
    }

    private void OnDisable()
    {
        if (stress != null)
        {
            stress.PolaritySnapped -= OnPolaritySnap;
            stress.LockoutDenied -= OnLockoutDenied;
            stress.OnMeltdownOverload.RemoveListener(OnMeltdownOverload);
            stress.OnCrystalShatterOverload.RemoveListener(OnCrystalShatterOverload);
            stress.OnEnterStrainedBrittle.RemoveListener(OnEnterStrainedBrittle);
            stress.OnEnterCriticalBrittle.RemoveListener(OnEnterCriticalBrittle);
        }

        if (respawn != null)
        {
            respawn.Died -= OnPlayerDied;
            respawn.Respawned -= OnPlayerRespawned;
        }

        if (SoundManager.instance != null) SoundManager.PlayWallSlide(false);
    }

    private void Update()
    {
        if (controller == null || rb == null || (respawn != null && respawn.IsDead))
        {
            if (wasWallSliding)
            {
                wasWallSliding = false;
                if (SoundManager.instance != null) SoundManager.PlayWallSlide(false);
            }
            return;
        }

        // --- Walking Footsteps ---
        if (controller.IsGrounded && !controller.IsDashing)
        {
            float speedX = Mathf.Abs(rb.linearVelocity.x);
            if (speedX >= minMoveSpeedForFootstep)
            {
                footstepTimer -= Time.deltaTime;
                if (footstepTimer <= 0f)
                {
                    SoundManager.PlayFootstep();
                    footstepTimer = footstepInterval;
                }
            }
            else
            {
                footstepTimer = 0.05f; // Ready to step as soon as movement starts
            }
        }
        else
        {
            footstepTimer = 0.1f;
        }

        // --- Wall Slide Looping Audio ---
        bool isSliding = controller.IsGripping && rb.linearVelocity.y < -0.1f && !controller.IsGrounded;
        if (isSliding != wasWallSliding)
        {
            wasWallSliding = isSliding;
            SoundManager.PlayWallSlide(isSliding);
        }
    }

    // ------------------------------------------------------------------ Event Callbacks
    private void OnPolaritySnap() => SoundManager.PlayPolaritySnap();

    private void OnLockoutDenied(LockoutKind kind)
    {
        bool isLiquid = (kind <= LockoutKind.NoClimb);
        SoundManager.PlayLockoutDenied(isLiquid);
    }

    private void OnMeltdownOverload() => SoundManager.PlayDeath(isCrystal: false);

    private void OnCrystalShatterOverload() => SoundManager.PlayDeath(isCrystal: true);

    private void OnEnterStrainedBrittle() => SoundManager.PlayBodyCrack();

    private void OnEnterCriticalBrittle() => SoundManager.PlayBodyCrack();

    private void OnPlayerDied()
    {
        if (SoundManager.instance != null) SoundManager.PlayWallSlide(false);
        // If stress didn't already trigger overload death (e.g. hazard death), play default death
        if (stress == null || (!stress.IsLiquidLocked && !stress.IsBrittleLocked))
        {
            SoundManager.PlayDeath(isCrystal: false);
        }
    }

    private void OnPlayerRespawned()
    {
        wasWallSliding = false;
        SoundManager.PlayRespawn();
    }
}
