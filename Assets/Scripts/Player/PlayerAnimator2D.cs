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
        if (crystalCrackOverlay != null)
            crystalCrackOverlay.enabled = false;
    }

    private void OnEnable()
    {
        if (stress == null) stress = GetComponentInParent<PlayerStress>();
        if (respawn == null) respawn = GetComponentInParent<PlayerRespawn>();

        if (stress != null)
        {
            stress.OnLiquidLockoutFail.AddListener(OnLiquidLockoutFail);
            stress.OnBrittleWallSlip.AddListener(OnBrittleWallSlip);
        }
        if (respawn != null) respawn.Respawned += OnPlayerRespawned;
    }

    private void OnDisable()
    {
        if (stress != null)
        {
            stress.OnLiquidLockoutFail.RemoveListener(OnLiquidLockoutFail);
            stress.OnBrittleWallSlip.RemoveListener(OnBrittleWallSlip);
        }
        if (respawn != null) respawn.Respawned -= OnPlayerRespawned;
    }

    private bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;

    private void LateUpdate()
    {
        if (controller == null || rb == null) return;

        UpdateFacingFlip();
        if (!HasAnimator) return;

        Vector2 vel = rb.linearVelocity;
        bool isDead = (respawn != null && respawn.IsDead) || (stress != null && stress.IsOverloaded);

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
        if (!HasAnimator) return;
        animator.ResetTrigger(TrigDoubleJump);
        animator.ResetTrigger(TrigDashFail);
        animator.ResetTrigger(TrigBrittleSlip);
        animator.SetBool(ParamIsDead, false);
        animator.Play(StateIdle, 0, 0f);
    }
}
