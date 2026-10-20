using UnityEngine;

/// <summary>
/// Connects PlayerController2D, PlayerStress, and PlayerRespawn to the character's Animator (_Player.controller)
/// and SpriteRenderer. Handles 0-latency state switching, sprite flipping, and Dual-Polarity visual feedback.
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

    [Header("Animation Tuning")]
    [Tooltip("Horizontal speed above which 'Idle' transitions to 'Walk'.")]
    [SerializeField] private float walkSpeedThreshold = 0.15f;
    [Tooltip("Horizontal speed above which 'Walk' transitions to 'Run'.")]
    [SerializeField] private float runSpeedThreshold = 4.5f;
    [Tooltip("How long lockout fail animations ('Dash(ไม่ได้)' / 'Slime_ร่างแข็ง') display when triggered.")]
    [SerializeField] private float lockoutAnimDuration = 0.22f;
    [Tooltip("Invert sprite horizontal flip if artwork faces left by default (false = artwork faces right).")]
    [SerializeField] private bool artworkFacesLeft = false;

    // Animator state hashes matching Assets/Player_Controller/_Player.controller
    private static readonly int StateIdle        = Animator.StringToHash("Idle");
    private static readonly int StateWalk        = Animator.StringToHash("Walk");
    private static readonly int StateRun         = Animator.StringToHash("Run");
    private static readonly int StateJump        = Animator.StringToHash("Jump");
    private static readonly int StateDoubleJump  = Animator.StringToHash("DB_Jump");
    private static readonly int StateDash        = Animator.StringToHash("Dash");
    private static readonly int StateDashFail    = Animator.StringToHash("Dash(ไม่ได้)");
    private static readonly int StateWallHold    = Animator.StringToHash("Wall_climb01");
    private static readonly int StateWallClimb   = Animator.StringToHash("Wall_climb02");
    private static readonly int StateWallStrain  = Animator.StringToHash("Wall_climb03");
    private static readonly int StateDead        = Animator.StringToHash("Dead");
    private static readonly int StateBrittle     = Animator.StringToHash("Slime_ร่างแข็ง");

    // Animator parameter hashes
    private static readonly int ParamSpeedX      = Animator.StringToHash("SpeedX");
    private static readonly int ParamVelocityY   = Animator.StringToHash("VelocityY");
    private static readonly int ParamStress      = Animator.StringToHash("Stress");
    private static readonly int ParamIsGrounded  = Animator.StringToHash("IsGrounded");
    private static readonly int ParamIsGripping  = Animator.StringToHash("IsGripping");
    private static readonly int ParamIsDashing   = Animator.StringToHash("IsDashing");
    private static readonly int ParamIsDead      = Animator.StringToHash("IsDead");

    private int currentStateHash;
    private bool wasCanDoubleJump = true;
    private bool playingDoubleJumpAnim;
    private float lockoutOverrideTimer;
    private int lockoutOverrideStateHash;

    public void BindReferences(
        PlayerController2D ctrl,
        PlayerStress playerStress,
        PlayerRespawn playerRespawn,
        Rigidbody2D body,
        Animator anim,
        SpriteRenderer sr,
        SpriteRenderer crackOverlay)
    {
        controller = ctrl;
        stress = playerStress;
        respawn = playerRespawn;
        rb = body;
        animator = anim;
        spriteRenderer = sr;
        crystalCrackOverlay = crackOverlay;
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
    }

    private void OnEnable()
    {
        if (stress == null) stress = GetComponentInParent<PlayerStress>();
        if (respawn == null) respawn = GetComponentInParent<PlayerRespawn>();

        if (stress != null)
        {
            stress.OnLiquidLockoutFail.AddListener(OnLiquidLockoutFail);
            stress.OnBrittleWallSlip.AddListener(OnBrittleWallSlip);
            stress.OnCrystalShatterOverload.AddListener(OnCrystalShatterOverload);
            stress.OnMeltdownOverload.AddListener(OnMeltdownOverload);
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
            stress.OnLiquidLockoutFail.RemoveListener(OnLiquidLockoutFail);
            stress.OnBrittleWallSlip.RemoveListener(OnBrittleWallSlip);
            stress.OnCrystalShatterOverload.RemoveListener(OnCrystalShatterOverload);
            stress.OnMeltdownOverload.RemoveListener(OnMeltdownOverload);
        }

        if (respawn != null)
        {
            respawn.Died -= OnPlayerDied;
            respawn.Respawned -= OnPlayerRespawned;
        }
    }

    private void LateUpdate()
    {
        if (controller == null || rb == null) return;

        UpdateFacingFlip();
        UpdateAnimatorParameters();

        int targetState = ResolveTargetState();
        PlayState(targetState);
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

    private void UpdateAnimatorParameters()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        Vector2 vel = rb.linearVelocity;
        animator.SetFloat(ParamSpeedX, Mathf.Abs(vel.x));
        animator.SetFloat(ParamVelocityY, vel.y);
        animator.SetFloat(ParamStress, stress != null ? stress.Current : 0f);
        animator.SetBool(ParamIsGrounded, controller.IsGrounded);
        animator.SetBool(ParamIsGripping, controller.IsGripping);
        animator.SetBool(ParamIsDashing, controller.IsDashing);
        animator.SetBool(ParamIsDead, respawn != null && respawn.IsDead);
    }

    private int ResolveTargetState()
    {
        // 1. Dead / Overload state
        bool isDead = (respawn != null && respawn.IsDead) || (stress != null && stress.IsOverloaded);
        if (isDead)
        {
            if (stress != null && stress.Current <= -100f)
                return StateBrittle;
            return StateDead;
        }

        // Track Double-Jump consumption while airborne
        if (controller.IsGrounded || controller.IsGripping || controller.IsDashing)
        {
            playingDoubleJumpAnim = false;
        }
        else if (wasCanDoubleJump && !controller.CanDoubleJump)
        {
            playingDoubleJumpAnim = true;
        }
        wasCanDoubleJump = controller.CanDoubleJump;

        // 2. Brief lockout feedback override ("Dash(ไม่ได้)" or "Slime_ร่างแข็ง")
        if (lockoutOverrideTimer > 0f)
        {
            lockoutOverrideTimer -= Time.deltaTime;
            if (!controller.IsDashing)
                return lockoutOverrideStateHash;
        }

        // 3. Dash state
        if (controller.IsDashing)
            return StateDash;

        // 4. Wall Grip / Climb / Slide state
        if (controller.IsGripping)
        {
            float vy = rb.linearVelocity.y;
            bool isStrainedTensile = stress != null && stress.Current >= stress.StrainedThreshold;

            if (vy > 0.1f)
                return isStrainedTensile ? StateWallStrain : StateWallClimb;

            return isStrainedTensile ? StateWallStrain : StateWallHold;
        }

        // 5. Airborne (Jump vs Double-Jump)
        if (!controller.IsGrounded)
            return playingDoubleJumpAnim ? StateDoubleJump : StateJump;

        // 6. Grounded (Idle / Walk / Run)
        float speedX = Mathf.Abs(rb.linearVelocity.x);
        if (speedX >= runSpeedThreshold)
            return StateRun;
        if (speedX >= walkSpeedThreshold)
            return StateWalk;

        return StateIdle;
    }

    private void PlayState(int stateHash, bool forceRestart = false)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (!forceRestart && currentStateHash == stateHash) return;

        currentStateHash = stateHash;
        animator.Play(stateHash, 0, 0f);
    }

    private void OnLiquidLockoutFail()
    {
        lockoutOverrideStateHash = StateDashFail;
        lockoutOverrideTimer = lockoutAnimDuration;
        PlayState(StateDashFail, forceRestart: true);
    }

    private void OnBrittleWallSlip()
    {
        lockoutOverrideStateHash = StateBrittle;
        lockoutOverrideTimer = lockoutAnimDuration;
        PlayState(StateBrittle, forceRestart: true);
    }

    private void OnCrystalShatterOverload()
    {
        lockoutOverrideTimer = 0f;
        PlayState(StateBrittle, forceRestart: true);
    }

    private void OnMeltdownOverload()
    {
        lockoutOverrideTimer = 0f;
        PlayState(StateDead, forceRestart: true);
    }

    private void OnPlayerDied()
    {
        lockoutOverrideTimer = 0f;
        int deadState = (stress != null && stress.Current <= -100f) ? StateBrittle : StateDead;
        PlayState(deadState, forceRestart: true);
    }

    private void OnPlayerRespawned()
    {
        lockoutOverrideTimer = 0f;
        playingDoubleJumpAnim = false;
        wasCanDoubleJump = true;
        PlayState(StateIdle, forceRestart: true);
    }
}
