using UnityEngine;

/// <summary>
/// Feeds PlayerController2D / PlayerStress / PlayerRespawn state into the Animator parameters of
/// Assets/Player_Controller/_Player.controller. All state switching is done by the transitions inside the
/// Animator graph (open the Animator window to see / tweak them). This script also flips the sprite.
///
/// Parameters driven:
///   Float   SpeedX, VelocityY, Stress
///   Bool    IsGrounded, IsGripping, IsDashing, IsDead
///   Trigger DoubleJump, DashFail, BrittleSlip
/// </summary>
public class PlayerAnimator2D : MonoBehaviour
{
    [Header("References (Auto-bound by PlayerSetup)")]
    [SerializeField] private PlayerController2D controller;
    [SerializeField] private PlayerStress stress;
    [SerializeField] private PlayerRespawn respawn;
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private SpriteRenderer crystalCrackOverlay;

    [Header("Movement VFX")]
    [Tooltip("Particle system emitted when the player runs on the ground.")]
    [SerializeField] private ParticleSystem runSlimeParticles;
    [Tooltip("Particle system emitted when the player dashes (burst/droplets effect).")]
    [SerializeField] private ParticleSystem dashParticles;
    [Tooltip("Particle system emitted when the player dashes (ribbon/trail effect).")]
    [SerializeField] private ParticleSystem dashTrailParticles;
    [Tooltip("TrailRenderer that only emits while the player is dashing.")]
    [SerializeField] private TrailRenderer dashTrailRenderer;
    [Tooltip("Minimum horizontal velocity to trigger the run particle effect.")]
    [SerializeField] private float minRunSpeed = 0.5f;
    [Tooltip("Particle offset when facing right (X is flipped when facing left).")]
    [SerializeField] private Vector3 runParticleOffset = new Vector3(-0.35f, -0.66f, 0f);

    [Header("Wall Jump VFX")]
    [Tooltip("Particle system emitted when the player performs a wall jump.")]
    [SerializeField] private ParticleSystem wallJumpParticles;
    [Tooltip("Particle offset when the wall is on the right (+X). Flipped on X when the wall is on the left.")]
    [SerializeField] private Vector3 wallJumpParticleOffset = new Vector3(0.44f, 0f, 0f);
    [Tooltip("If true, particles spray away from the wall into the room. If false, spray towards the wall.")]
    [SerializeField] private bool sprayAwayFromWall = true;
    [Tooltip("Burst count override. Set to 0 to use the ParticleSystem emission burst setting.")]
    [SerializeField] private int wallJumpBurstCount = 0;

    [Header("Death VFX")]
    [Tooltip("Particle system emitted when the player dies (e.g. Dead Particle).")]
    [SerializeField] private ParticleSystem deadParticles;
    [Tooltip("Particle offset relative to player center on death.")]
    [SerializeField] private Vector3 deadParticleOffset = new Vector3(0f, -0.11f, 0f);
    [Tooltip("Stress threshold below which death is considered negative death. If stress <= threshold, deadParticles will not play.")]
    [SerializeField] private float negativeDeathThreshold = -99f;

    [Header("Wall Slide VFX")]
    [Tooltip("Particle system emitted continuously while sliding down a wall.")]
    [SerializeField] private ParticleSystem wallSlideParticles;
    [Tooltip("Particle offset when wall is on the right (+X). Flipped on X when wall is on the left.")]
    [SerializeField] private Vector3 wallSlideParticleOffset = new Vector3(0.44f, 0f, 0f);
    [Tooltip("Minimum downward speed (-vel.y) to trigger wall slide particles.")]
    [SerializeField] private float minWallSlideSpeed = 0.1f;
    [Tooltip("Tilt angle (in degrees) away from the wall while sliding down. Set to 0 to keep it straight vertical without tilting.")]
    [SerializeField] private float wallSlideTiltAngle = 0f;
    [Tooltip("If true, requires grip to be held to emit wall slide particles. If false, touching a wall while sliding down is enough.")]
    [SerializeField] private bool requireGripForWallSlide = true;

    [Header("Facing")]
    [Tooltip("Invert sprite horizontal flip if artwork faces left by default (false = artwork faces right).")]
    [SerializeField] private bool artworkFacesLeft = false;

    // Animator parameter hashes (must match _Player.controller)
    private static readonly int ParamSpeedX      = Animator.StringToHash("SpeedX");
    private static readonly int ParamVelocityY   = Animator.StringToHash("VelocityY");
    private static readonly int ParamStress      = Animator.StringToHash("Stress");
    private static readonly int ParamIsGrounded  = Animator.StringToHash("IsGrounded");
    private static readonly int ParamIsGripping  = Animator.StringToHash("IsGripping");
    private static readonly int ParamIsDashing   = Animator.StringToHash("IsDashing");
    private static readonly int ParamIsDead      = Animator.StringToHash("IsDead");
    private static readonly int TrigDoubleJump   = Animator.StringToHash("DoubleJump");
    private static readonly int TrigDashFail     = Animator.StringToHash("DashFail");
    private static readonly int TrigBrittleSlip  = Animator.StringToHash("BrittleSlip");
    private static readonly int StateIdle        = Animator.StringToHash("Idle");

    private bool wasCanDoubleJump = true;

    public void BindReferences(
        PlayerController2D ctrl,
        PlayerStress playerStress,
        PlayerRespawn playerRespawn,
        Rigidbody2D body,
        Animator anim,
        SpriteRenderer sr,
        SpriteRenderer crackOverlay,
        ParticleSystem slimeParticles = null,
        ParticleSystem dashPs = null,
        ParticleSystem dashTrailPs = null,
        ParticleSystem wallJumpPs = null,
        ParticleSystem deadPs = null,
        ParticleSystem wallSlidePs = null)
    {
        controller = ctrl;
        stress = playerStress;
        respawn = playerRespawn;
        rb = body;
        animator = anim;
        spriteRenderer = sr;
        crystalCrackOverlay = crackOverlay;
        if (slimeParticles != null) runSlimeParticles = slimeParticles;
        if (dashPs != null) dashParticles = dashPs;
        if (dashTrailPs != null) dashTrailParticles = dashTrailPs;
        if (wallJumpPs != null) wallJumpParticles = wallJumpPs;
        if (deadPs != null) deadParticles = deadPs;
        if (wallSlidePs != null) wallSlideParticles = wallSlidePs;
    }

    private void Reset()
    {
        AutoBindEditor();
    }

    private void OnValidate()
    {
        AutoBindEditor();
    }

    private void AutoBindEditor()
    {
        if (controller == null) controller = GetComponentInParent<PlayerController2D>();
        if (stress == null) stress = GetComponentInParent<PlayerStress>();
        if (respawn == null) respawn = GetComponentInParent<PlayerRespawn>();
        if (rb == null) rb = GetComponentInParent<Rigidbody2D>();

        if (runSlimeParticles == null)
        {
            Transform slimeTf = transform.Find("Slime Particle");
            if (slimeTf == null) slimeTf = transform.Find("Walking Slime Particle");
            if (slimeTf == null && transform.parent != null)
            {
                slimeTf = transform.parent.Find("Slime Particle");
                if (slimeTf == null) slimeTf = transform.parent.Find("Walking Slime Particle");
            }
            if (slimeTf != null)
                runSlimeParticles = slimeTf.GetComponent<ParticleSystem>();
        }

        if (dashParticles == null)
        {
            Transform dashTf = transform.Find("Dash Particle");
            if (dashTf == null) dashTf = transform.Find("Dash Slime VFX");
            if (dashTf == null && transform.parent != null)
            {
                dashTf = transform.parent.Find("Dash Particle");
                if (dashTf == null) dashTf = transform.parent.Find("Dash Slime VFX");
            }
            if (dashTf != null)
                dashParticles = dashTf.GetComponent<ParticleSystem>();
        }

        if (dashTrailParticles == null)
        {
            Transform trailTf = transform.Find("DashTrail Slime VFX");
            if (trailTf == null) trailTf = transform.Find("DashTrail");
            if (trailTf == null && transform.parent != null)
            {
                trailTf = transform.parent.Find("DashTrail Slime VFX");
                if (trailTf == null) trailTf = transform.parent.Find("DashTrail");
            }
            if (trailTf != null)
                dashTrailParticles = trailTf.GetComponent<ParticleSystem>();
        }

        if (wallJumpParticles == null)
        {
            Transform wjTf = transform.Find("WallJump Particle");
            if (wjTf == null) wjTf = transform.Find("Wall Jump Particle");
            if (wjTf == null) wjTf = transform.Find("WallJump VFX");
            if (wjTf == null && transform.parent != null)
            {
                wjTf = transform.parent.Find("WallJump Particle");
                if (wjTf == null) wjTf = transform.parent.Find("Wall Jump Particle");
                if (wjTf == null) wjTf = transform.parent.Find("WallJump VFX");
            }
            if (wjTf != null)
                wallJumpParticles = wjTf.GetComponent<ParticleSystem>();
        }

        if (deadParticles == null)
        {
            Transform deadTf = transform.Find("Dead Particle");
            if (deadTf == null) deadTf = transform.Find("Death Particle");
            if (deadTf == null && transform.parent != null)
            {
                deadTf = transform.parent.Find("Dead Particle");
                if (deadTf == null) deadTf = transform.parent.Find("Death Particle");
            }
            if (deadTf != null)
                deadParticles = deadTf.GetComponent<ParticleSystem>();
        }

        if (wallSlideParticles == null)
        {
            Transform wsTf = transform.Find("WallSlide Particle");
            if (wsTf == null) wsTf = transform.Find("Wall Slide Particle");
            if (wsTf == null) wsTf = transform.Find("WallSlide VFX");
            if (wsTf == null && transform.parent != null)
            {
                wsTf = transform.parent.Find("WallSlide Particle");
                if (wsTf == null) wsTf = transform.parent.Find("Wall Slide Particle");
                if (wsTf == null) wsTf = transform.parent.Find("WallSlide VFX");
            }
            if (wsTf != null)
                wallSlideParticles = wsTf.GetComponent<ParticleSystem>();
        }

        if (dashTrailRenderer == null)
            dashTrailRenderer = FindDashTrailRenderer();
    }

    private TrailRenderer FindDashTrailRenderer()
    {
        Transform trailTf = transform.Find("DashTrail Slime VFX");
        if (trailTf == null) trailTf = transform.Find("DashTrail");
        if (trailTf == null && transform.parent != null)
        {
            trailTf = transform.parent.Find("DashTrail Slime VFX");
            if (trailTf == null) trailTf = transform.parent.Find("DashTrail");
        }
        return trailTf != null ? trailTf.GetComponent<TrailRenderer>() : null;
    }

    private void ResetDashTrail()
    {
        if (dashTrailRenderer == null) return;
        dashTrailRenderer.emitting = false;
        dashTrailRenderer.Clear();
    }

    private void Awake()
    {
        if (controller == null) controller = GetComponentInParent<PlayerController2D>();
        if (stress == null) stress = GetComponentInParent<PlayerStress>();
        if (respawn == null) respawn = GetComponentInParent<PlayerRespawn>();
        if (rb == null) rb = GetComponentInParent<Rigidbody2D>();

        Transform vis = transform.Find("Visual");
        if (vis == null && transform.name == "Visual") vis = transform;

        if (animator == null)
            animator = vis != null ? vis.GetComponent<Animator>() : GetComponentInChildren<Animator>();
        if (spriteRenderer == null)
            spriteRenderer = vis != null ? vis.GetComponent<SpriteRenderer>() : GetComponentInChildren<SpriteRenderer>();

        if (crystalCrackOverlay == null && vis != null)
        {
            Transform overlayTf = vis.Find("CrystalOverlay");
            if (overlayTf != null) crystalCrackOverlay = overlayTf.GetComponent<SpriteRenderer>();
        }
        if (crystalCrackOverlay != null)
            crystalCrackOverlay.enabled = false;

        if (runSlimeParticles == null)
        {
            Transform slimeTf = transform.Find("Slime Particle");
            if (slimeTf == null) slimeTf = transform.Find("Walking Slime Particle");
            if (slimeTf == null && transform.parent != null)
            {
                slimeTf = transform.parent.Find("Slime Particle");
                if (slimeTf == null) slimeTf = transform.parent.Find("Walking Slime Particle");
            }
            if (slimeTf != null)
                runSlimeParticles = slimeTf.GetComponent<ParticleSystem>();
        }

        if (dashParticles == null)
        {
            Transform dashTf = transform.Find("Dash Particle");
            if (dashTf == null) dashTf = transform.Find("Dash Slime VFX");
            if (dashTf == null && transform.parent != null)
            {
                dashTf = transform.parent.Find("Dash Particle");
                if (dashTf == null) dashTf = transform.parent.Find("Dash Slime VFX");
            }
            if (dashTf != null)
                dashParticles = dashTf.GetComponent<ParticleSystem>();
        }

        if (dashTrailParticles == null)
        {
            Transform trailTf = transform.Find("DashTrail Slime VFX");
            if (trailTf == null) trailTf = transform.Find("DashTrail");
            if (trailTf == null && transform.parent != null)
            {
                trailTf = transform.parent.Find("DashTrail Slime VFX");
                if (trailTf == null) trailTf = transform.parent.Find("DashTrail");
            }
            if (trailTf != null)
                dashTrailParticles = trailTf.GetComponent<ParticleSystem>();
        }

        if (wallJumpParticles == null)
        {
            Transform wjTf = transform.Find("WallJump Particle");
            if (wjTf == null) wjTf = transform.Find("Wall Jump Particle");
            if (wjTf == null) wjTf = transform.Find("WallJump VFX");
            if (wjTf == null && transform.parent != null)
            {
                wjTf = transform.parent.Find("WallJump Particle");
                if (wjTf == null) wjTf = transform.parent.Find("Wall Jump Particle");
                if (wjTf == null) wjTf = transform.parent.Find("WallJump VFX");
            }
            if (wjTf != null)
                wallJumpParticles = wjTf.GetComponent<ParticleSystem>();
        }

        if (deadParticles == null)
        {
            Transform deadTf = transform.Find("Dead Particle");
            if (deadTf == null) deadTf = transform.Find("Death Particle");
            if (deadTf == null && transform.parent != null)
            {
                deadTf = transform.parent.Find("Dead Particle");
                if (deadTf == null) deadTf = transform.parent.Find("Death Particle");
            }
            if (deadTf != null)
                deadParticles = deadTf.GetComponent<ParticleSystem>();
        }

        if (wallSlideParticles == null)
        {
            Transform wsTf = transform.Find("WallSlide Particle");
            if (wsTf == null) wsTf = transform.Find("Wall Slide Particle");
            if (wsTf == null) wsTf = transform.Find("WallSlide VFX");
            if (wsTf == null && transform.parent != null)
            {
                wsTf = transform.parent.Find("WallSlide Particle");
                if (wsTf == null) wsTf = transform.parent.Find("Wall Slide Particle");
                if (wsTf == null) wsTf = transform.parent.Find("WallSlide VFX");
            }
            if (wsTf != null)
                wallSlideParticles = wsTf.GetComponent<ParticleSystem>();
        }

        if (runSlimeParticles != null)
        {
            if (runParticleOffset == Vector3.zero)
                runParticleOffset = runSlimeParticles.transform.localPosition;

            var main = runSlimeParticles.main;
            main.playOnAwake = false;
            var em = runSlimeParticles.emission;
            em.enabled = false;
            runSlimeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (dashParticles != null)
        {
            var main = dashParticles.main;
            main.playOnAwake = false;
            var em = dashParticles.emission;
            em.enabled = false;
            dashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (dashTrailParticles != null)
        {
            dashTrailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (wallJumpParticles != null)
        {
            var main = wallJumpParticles.main;
            main.playOnAwake = false;
            wallJumpParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (deadParticles != null)
        {
            var main = deadParticles.main;
            main.playOnAwake = false;
            deadParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (wallSlideParticles != null)
        {
            var main = wallSlideParticles.main;
            main.playOnAwake = false;
            var em = wallSlideParticles.emission;
            em.enabled = false;
            wallSlideParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (dashTrailRenderer == null)
            dashTrailRenderer = FindDashTrailRenderer();
        ResetDashTrail();
    }

    private void OnEnable()
    {
        if (controller == null) controller = GetComponentInParent<PlayerController2D>();
        if (stress == null) stress = GetComponentInParent<PlayerStress>();
        if (respawn == null) respawn = GetComponentInParent<PlayerRespawn>();

        if (controller != null)
        {
            controller.DashStarted += OnDashStarted;
            controller.WallJumped += OnWallJumped;
        }
        if (stress != null)
        {
            stress.OnLiquidLockoutFail.AddListener(OnLiquidLockoutFail);
            stress.OnBrittleWallSlip.AddListener(OnBrittleWallSlip);
        }
        if (respawn != null)
        {
            respawn.Died += OnPlayerDied;
            respawn.Respawned += OnPlayerRespawned;
        }
    }

    private void OnDisable()
    {
        if (controller != null)
        {
            controller.DashStarted -= OnDashStarted;
            controller.WallJumped -= OnWallJumped;
        }
        if (stress != null)
        {
            stress.OnLiquidLockoutFail.RemoveListener(OnLiquidLockoutFail);
            stress.OnBrittleWallSlip.RemoveListener(OnBrittleWallSlip);
        }
        if (respawn != null)
        {
            respawn.Died -= OnPlayerDied;
            respawn.Respawned -= OnPlayerRespawned;
        }

        if (runSlimeParticles != null)
        {
            var em = runSlimeParticles.emission;
            em.enabled = false;
            runSlimeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (dashParticles != null)
        {
            var em = dashParticles.emission;
            em.enabled = false;
            dashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (dashTrailParticles != null)
        {
            dashTrailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (wallJumpParticles != null)
        {
            wallJumpParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (deadParticles != null)
        {
            deadParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (wallSlideParticles != null)
        {
            var em = wallSlideParticles.emission;
            em.enabled = false;
            wallSlideParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        ResetDashTrail();
    }

    private bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;

    private void LateUpdate()
    {
        if (controller == null || rb == null) return;

        Vector2 vel = rb.linearVelocity;
        bool isDead = (respawn != null && respawn.IsDead) || (stress != null && stress.IsOverloaded);

        UpdateFacingFlip();
        UpdateRunParticles(vel, isDead);
        UpdateDashParticles(isDead);
        UpdateWallSlideParticles(vel, isDead);

        if (!HasAnimator) return;

        animator.SetFloat(ParamSpeedX, Mathf.Abs(vel.x));
        animator.SetFloat(ParamVelocityY, vel.y);
        animator.SetFloat(ParamStress, stress != null ? stress.Current : 0f);
        animator.SetBool(ParamIsGrounded, controller.IsGrounded);
        animator.SetBool(ParamIsGripping, controller.IsGripping);
        animator.SetBool(ParamIsDashing, controller.IsDashing);
        animator.SetBool(ParamIsDead, isDead);

        // Double-Jump: fires the moment the air-jump quota is consumed while airborne.
        bool canDJ = controller.CanDoubleJump;
        if (!isDead && !controller.IsGrounded && !controller.IsGripping && wasCanDoubleJump && !canDJ)
            animator.SetTrigger(TrigDoubleJump);
        wasCanDoubleJump = canDJ;

        // Don't let a stale trigger fire later (e.g. after landing).
        if (controller.IsGrounded || controller.IsGripping)
            animator.ResetTrigger(TrigDoubleJump);
    }

    private void UpdateRunParticles(Vector2 vel, bool isDead)
    {
        if (runSlimeParticles == null) return;

        // Keep particle emitter aligned with player facing
        int dir = controller != null ? controller.Facing : 1;
        runSlimeParticles.transform.localPosition = new Vector3(runParticleOffset.x * dir, runParticleOffset.y, runParticleOffset.z);
        runSlimeParticles.transform.localEulerAngles = new Vector3(0f, dir > 0 ? 0f : 180f, 0f);

        bool isRunning = !isDead
            && controller != null
            && controller.IsGrounded
            && !controller.IsGripping
            && !controller.IsDashing
            && Mathf.Abs(vel.x) >= minRunSpeed;

        var em = runSlimeParticles.emission;
        if (em.enabled != isRunning)
        {
            em.enabled = isRunning;
        }

        if (isRunning)
        {
            if (!runSlimeParticles.isPlaying)
                runSlimeParticles.Play();
        }
    }

    private void OnDashStarted(Vector2 dashDir, bool isEmergency)
    {
        if (dashParticles != null)
        {
            if (dashDir != Vector2.zero)
            {
                float angle = Mathf.Atan2(-dashDir.y, -dashDir.x) * Mathf.Rad2Deg;
                dashParticles.transform.localEulerAngles = new Vector3(0f, 0f, angle);
            }

            int burstCount = isEmergency ? 25 : 15;
            dashParticles.Emit(burstCount);

            if (!dashParticles.isPlaying)
                dashParticles.Play();
        }

        if (dashTrailParticles != null)
        {
            dashTrailParticles.Clear();
            dashTrailParticles.Play();
        }

        if (dashTrailRenderer != null)
        {
            dashTrailRenderer.Clear();          // drop old points so the trail starts at the dash origin
            dashTrailRenderer.emitting = true;
        }
    }

    private void UpdateDashParticles(bool isDead)
    {
        if (controller == null) return;

        bool isDashing = !isDead && controller.IsDashing;
        Vector2 dir = controller.DashDirection;

        if (dashParticles != null)
        {
            var em = dashParticles.emission;
            if (em.enabled != isDashing)
            {
                em.enabled = isDashing;
            }

            if (isDashing)
            {
                if (dir != Vector2.zero)
                {
                    float angle = Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg;
                    dashParticles.transform.localEulerAngles = new Vector3(0f, 0f, angle);
                }

                if (!dashParticles.isPlaying)
                    dashParticles.Play();
            }
        }

        if (dashTrailParticles != null)
        {
            if (isDashing)
            {
                if (!dashTrailParticles.isPlaying)
                    dashTrailParticles.Play();
            }
            else
            {
                if (dashTrailParticles.isPlaying)
                    dashTrailParticles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        // Emit only while dashing; existing points fade out naturally over TrailRenderer.time.
        if (dashTrailRenderer != null && dashTrailRenderer.emitting != isDashing)
            dashTrailRenderer.emitting = isDashing;
    }

    private void UpdateFacingFlip()
    {
        if (spriteRenderer == null) return;

        int dir = (controller.IsGripping && controller.WallSide != 0)
            ? controller.WallSide
            : controller.Facing;

        bool flip = artworkFacesLeft ? (dir > 0) : (dir < 0);
        spriteRenderer.flipX = flip;

        if (crystalCrackOverlay != null)
            crystalCrackOverlay.flipX = flip;
    }

    private void OnLiquidLockoutFail()
    {
        if (HasAnimator) animator.SetTrigger(TrigDashFail);
    }

    private void OnBrittleWallSlip()
    {
        if (HasAnimator) animator.SetTrigger(TrigBrittleSlip);
    }

    private void OnPlayerRespawned()
    {
        wasCanDoubleJump = true;
        if (runSlimeParticles != null)
        {
            var em = runSlimeParticles.emission;
            em.enabled = false;
            runSlimeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (dashParticles != null)
        {
            var em = dashParticles.emission;
            em.enabled = false;
            dashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (dashTrailParticles != null)
        {
            dashTrailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (wallJumpParticles != null)
        {
            wallJumpParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (deadParticles != null)
        {
            deadParticles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        if (wallSlideParticles != null)
        {
            var em = wallSlideParticles.emission;
            em.enabled = false;
            wallSlideParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        ResetDashTrail();
        if (!HasAnimator) return;
        animator.ResetTrigger(TrigDoubleJump);
        animator.ResetTrigger(TrigDashFail);
        animator.ResetTrigger(TrigBrittleSlip);
        animator.SetBool(ParamIsDead, false);
        animator.Play(StateIdle, 0, 0f);
    }

    private void OnWallJumped(int wallSide)
    {
        if (wallJumpParticles == null) return;

        // Position at wall contact point
        float sign = wallSide > 0 ? 1f : -1f;
        wallJumpParticles.transform.localPosition = new Vector3(
            wallJumpParticleOffset.x * sign,
            wallJumpParticleOffset.y,
            wallJumpParticleOffset.z);

        // Rotation: default cone points in +X.
        // If wall is on right (wallSide > 0) and spraying away from wall, face -X (180 deg).
        // If wall is on left (wallSide < 0) and spraying away from wall, face +X (0 deg).
        // If spraying toward the wall, invert this behavior.
        bool faceLeft = sprayAwayFromWall ? (wallSide > 0) : (wallSide < 0);
        wallJumpParticles.transform.localEulerAngles = new Vector3(0f, faceLeft ? 180f : 0f, 0f);

        int burst = wallJumpBurstCount;
        if (burst <= 0)
        {
            var bursts = new ParticleSystem.Burst[wallJumpParticles.emission.burstCount];
            wallJumpParticles.emission.GetBursts(bursts);
            burst = (bursts != null && bursts.Length > 0 && bursts[0].maxCount > 0) ? bursts[0].maxCount : 30;
        }

        wallJumpParticles.Emit(burst);

        if (!wallJumpParticles.isPlaying)
            wallJumpParticles.Play();
    }

    private void OnPlayerDied()
    {
        // Stop locomotion / dash particles on death
        if (runSlimeParticles != null)
        {
            var em = runSlimeParticles.emission;
            em.enabled = false;
            runSlimeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (dashParticles != null)
        {
            var em = dashParticles.emission;
            em.enabled = false;
            dashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (dashTrailParticles != null)
        {
            dashTrailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (wallSlideParticles != null)
        {
            var em = wallSlideParticles.emission;
            em.enabled = false;
            wallSlideParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        ResetDashTrail();

        if (deadParticles == null) return;

        // ไม่ทำงานตอนตายติดลบ (เช่น Crystal Shatter Overload / Slime_ร่างแข็ง)
        bool isNegativeDeath = stress != null && stress.Current <= negativeDeathThreshold;
        if (isNegativeDeath) return;

        deadParticles.transform.localPosition = deadParticleOffset;

        var bursts = new ParticleSystem.Burst[deadParticles.emission.burstCount];
        deadParticles.emission.GetBursts(bursts);
        int burst = (bursts != null && bursts.Length > 0 && bursts[0].maxCount > 0) ? bursts[0].maxCount : 20;
        deadParticles.Emit(burst);

        if (!deadParticles.isPlaying)
            deadParticles.Play();
    }

    private void UpdateWallSlideParticles(Vector2 vel, bool isDead)
    {
        if (wallSlideParticles == null) return;

        int wallSide = controller != null ? controller.WallSide : 0;
        bool gripOk = !requireGripForWallSlide || (controller != null && controller.IsGripping);

        // Slide DOWN only: touching wall, airborne, moving downward (vel.y < -minWallSlideSpeed), never when climbing up
        bool isSlidingDown = !isDead
            && controller != null
            && gripOk
            && wallSide != 0
            && !controller.IsGrounded
            && !controller.IsDashing
            && vel.y < -minWallSlideSpeed;

        if (isSlidingDown)
        {
            // Position at wall contact point
            wallSlideParticles.transform.localPosition = new Vector3(
                wallSlideParticleOffset.x * wallSide,
                wallSlideParticleOffset.y,
                wallSlideParticleOffset.z);

            if (Mathf.Abs(wallSlideTiltAngle) > 0.01f)
            {
                float zAngle = wallSide > 0 ? wallSlideTiltAngle : -wallSlideTiltAngle;
                wallSlideParticles.transform.localEulerAngles = new Vector3(0f, 0f, zAngle);
            }
            else
            {
                wallSlideParticles.transform.localEulerAngles = Vector3.zero;
            }
        }

        var em = wallSlideParticles.emission;
        if (em.enabled != isSlidingDown)
        {
            em.enabled = isSlidingDown;
        }

        if (isSlidingDown)
        {
            if (!wallSlideParticles.isPlaying)
                wallSlideParticles.Play();
        }
        else if (wallSlideParticles.isPlaying && !em.enabled)
        {
            wallSlideParticles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
