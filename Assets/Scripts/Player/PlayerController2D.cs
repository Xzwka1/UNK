using UnityEngine;

/// <summary>
/// Precision-platformer player controller (Rigidbody2D, velocity driven).
/// Combines William's clean modular architecture (AssistsLogic, RefillDash)
/// with Earth's Dual-Polarity stress integration (Normal/Emergency Dash, Double Jump stress,
/// Wall-climb stress/lockout, Polarity Snap refill, Heavy Landing).
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D), typeof(PlayerStress))]
public class PlayerController2D : MonoBehaviour
{
    public enum MoveState { Normal, DashFreeze, Dashing }

    // ------------------------------------------------------------------ Inspector: Run & Movement
    [Header("Run (world units / second)")]
    [SerializeField] private float maxSpeed = 9f;
    [Tooltip("Seconds from 0 to max speed.")]
    [SerializeField] private float accelerationTime = 0.08f;
    [Tooltip("Seconds from max speed to a full stop (high friction, no sliding).")]
    [SerializeField] private float decelerationTime = 0.06f;
    [Tooltip("Reversing direction in mid-air flips the speed instantly instead of braking first.")]
    [SerializeField] private bool instantAirTurn = true;
    [Tooltip("Seconds from 0 to max speed while airborne (slightly softer than ground).")]
    [SerializeField] private float airAccelerationTime = 0.1f;
    [Tooltip("Seconds from max speed to a stop while airborne when no key is held (keeps momentum).")]
    [SerializeField] private float airDecelerationTime = 0.25f;

    [Header("Gravity")]
    [SerializeField] private float baseGravityScale = 3f;
    [Tooltip("Gravity multiplier while descending (heavy fall).")]
    [SerializeField] private float fallGravityMultiplier = 1.6f;
    [Tooltip("Gravity multiplier while rising with the jump button released (variable jump height).")]
    [SerializeField] private float lowJumpGravityMultiplier = 3f;
    [SerializeField] private float maxFallSpeed = 24f;
    [Tooltip("Near the jump apex (while holding jump) gravity is multiplied by this, for a floaty hang.")]
    [SerializeField] private float apexGravityMultiplier = 0.5f;
    [Tooltip("Vertical speed below which the apex bonus applies.")]
    [SerializeField] private float apexThreshold = 2f;

    [Header("Jump")]
    [Tooltip("Height reached when the jump button is held for the whole ascent.")]
    [SerializeField] private float jumpHeight = 3.2f;
    [SerializeField] private float airJumpHeight = 2.5f;
    [Tooltip("Air jumps per airtime. Refilled ONLY on the ground.")]
    [SerializeField] private int maxAirJumps = 1;

    [Header("Dash (Shift + WASD)")]
    [SerializeField] private float dashSpeed = 24f;
    [SerializeField] private float dashDuration = 0.15f;
    [Tooltip("Freeze frame right before the dash force is applied (0.025 - 0.05 s).")]
    [SerializeField, Range(0.025f, 0.05f)] private float dashFreezeDuration = 0.05f;
    [Tooltip("Normal dashes per airtime. Refilled on safe ground.")]
    [SerializeField] private int maxDashes = 1;
    [SerializeField] private float dashCooldown = 0.2f;
    [Tooltip("Fraction of the dash speed kept after the dash ends.")]
    [SerializeField, Range(0f, 1f)] private float dashExitSpeedMultiplier = 0.6f;
    [Tooltip("Re-read WASD when the freeze ends so slightly late diagonals still count.")]
    [SerializeField] private bool resampleDirectionAfterFreeze = true;

    [Header("Wall (hold Ctrl to grip)")]
    [SerializeField] private float climbSpeed = 3f;
    [SerializeField] private float slideSpeed = 4f;
    [SerializeField] private float wallJumpSpeedX = 9f;
    [SerializeField] private float wallJumpHeight = 2.5f;
    [Tooltip("Horizontal input is ignored this long after a wall-jump so the push-off is felt.")]
    [SerializeField] private float wallJumpControlLock = 0.12f;
    [SerializeField] private int maxWallJumps = 5;

    [Header("Heavy Landing Impact")]
    [Tooltip("Downward vertical speed threshold to trigger Heavy Landing impact on safe ground (-14f).")]
    [SerializeField] private float heavyLandingSpeedThreshold = -14f;

    [Header("Control assists")]
    [SerializeField] private AssistsLogic assists = new AssistsLogic();

    [Header("Environment checks")]
    [Tooltip("Layers that count as ground / walls. Triggers are always ignored.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float checkDistance = 0.06f;
    [Tooltip("Must match the art scale (used to convert corner-correction pixels to world units).")]
    [SerializeField] private float pixelsPerUnit = 16f;

    // ------------------------------------------------------------------ Components & State
    private Rigidbody2D rb;
    private CapsuleCollider2D col;
    private PlayerStress stress;

    private ContactFilter2D filter;
    private readonly Collider2D[] hits = new Collider2D[8];

    private MoveState state = MoveState.Normal;
    private bool controlEnabled = true;

    private PlayerInputState input;
    private bool jumpPressLatch, dashPressLatch;

    private bool grounded, lastGrounded, groundIsSafe, gripping, jumpRising, gripAllowed = true;
    private int wallSide, lastWallSide, facing = 1;

    private float dashFreezeTimer, dashTimer, dashCooldownTimer, wallJumpLockTimer;
    private int airJumpsLeft, dashesLeft, wallJumpsUsed;
    private bool canEmergencyDash = true;
    private Vector2 dashDir;
    private float lastVelY;

    // ------------------------------------------------------------------ Public Read-Only Properties
    public bool IsGrounded => grounded;
    public bool IsGripping => gripping;
    public bool IsDashing => state == MoveState.Dashing || state == MoveState.DashFreeze;
    public bool CanDoubleJump => airJumpsLeft > 0;
    public int WallSide => wallSide;
    public int Facing => facing;
    public MoveState State => state;
    public AssistsLogic Assists => assists;
    public Vector2 LinearVelocity => rb != null ? rb.linearVelocity : Vector2.zero;

    // ------------------------------------------------------------------ Lifecycle
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CapsuleCollider2D>();
        stress = GetComponent<PlayerStress>();
        ResetState();
    }

    private void OnEnable()
    {
        if (stress != null)
            stress.PolaritySnapped += OnPolaritySnapBonus;
    }

    private void OnDisable()
    {
        if (stress != null)
            stress.PolaritySnapped -= OnPolaritySnapBonus;
    }

    private void Update()
    {
        if (!controlEnabled)
        {
            input = default;
            jumpPressLatch = dashPressLatch = false;
            return;
        }

        input = PlayerInputReader.Read();
        if (input.JumpPressed) jumpPressLatch = true;
        if (input.DashPressed) dashPressLatch = true;
    }

    private void FixedUpdate()
    {
        if (!controlEnabled) return;

        float dt = Time.fixedDeltaTime;
        Vector2 vel = rb.linearVelocity;

        filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(groundMask);

        SenseEnvironment(vel);
        TickTimers(dt);

        // Detect Heavy Landing on impact with safe ground
        if (!lastGrounded && grounded && lastVelY <= heavyLandingSpeedThreshold)
        {
            if (stress != null)
                stress.ApplyHeavyLandingStress();
        }
        lastGrounded = grounded;
        lastVelY = vel.y;

        bool jumpPress = jumpPressLatch;
        bool dashPress = dashPressLatch;
        jumpPressLatch = dashPressLatch = false;
        if (jumpPress) assists.BufferJump();

        switch (state)
        {
            case MoveState.DashFreeze: TickDashFreeze(); break;
            case MoveState.Dashing: TickDashing(dt); break;
            default: TickNormal(vel, dt, jumpPress, dashPress); break;
        }
    }

    // ------------------------------------------------------------------ Public Control API
    public void SetControlEnabled(bool enabled)
    {
        controlEnabled = enabled;
        if (!enabled)
        {
            jumpPressLatch = dashPressLatch = false;
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
    }

    public void ResetState()
    {
        state = MoveState.Normal;
        assists.ResetTimers();
        dashFreezeTimer = dashTimer = dashCooldownTimer = wallJumpLockTimer = 0f;
        airJumpsLeft = maxAirJumps;
        dashesLeft = maxDashes;
        canEmergencyDash = true;
        wallJumpsUsed = 0;
        gripping = jumpRising = false;
        gripAllowed = true;
        jumpPressLatch = dashPressLatch = false;
        input = default;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = baseGravityScale;
        }
    }

    /// <summary>
    /// William's RefillDash API: Refills normal dash and emergency air dash instantly.
    /// Called by BreakableBarrier, RefillItem, and checkpoints.
    /// </summary>
    public void RefillDash()
    {
        dashesLeft = maxDashes;
        canEmergencyDash = true;
        dashCooldownTimer = 0f;
        if (state == MoveState.Dashing || state == MoveState.DashFreeze)
        {
            state = MoveState.Normal;
        }
    }

    private void OnPolaritySnapBonus()
    {
        dashesLeft = maxDashes;
        canEmergencyDash = true;
        airJumpsLeft = maxAirJumps;
        dashCooldownTimer = 0f;
    }

    // ------------------------------------------------------------------ Normal Movement
    private void TickNormal(Vector2 vel, float dt, bool jumpPress, bool dashPress)
    {
        float h = Mathf.Abs(input.Move.x) > 0.1f ? Mathf.Sign(input.Move.x) : 0f;
        if (h != 0f) facing = (int)h;

        // --- Dash Execution
        if (dashPress && dashCooldownTimer <= 0f)
        {
            if (stress != null && stress.IsLiquidLocked)
            {
                stress.NotifyLiquidLockoutFail();
            }
            else if (dashesLeft > 0)
            {
                BeginDash(false);
                return;
            }
            else if (!grounded && canEmergencyDash)
            {
                BeginDash(true);
                return;
            }
        }

        // --- Wall Grip eligibility & Brittle Lockout
        if (!input.GripHeld)
        {
            gripAllowed = true;
        }
        else if (wallSide == 0)
        {
            gripAllowed = false;
        }

        if (input.GripHeld && wallSide != 0 && stress != null && stress.IsBrittleLocked)
        {
            stress.NotifyBrittleWallSlip();
            gripping = false;
        }
        else
        {
            gripping = input.GripHeld && gripAllowed && wallSide != 0 && wallJumpLockTimer <= 0f;
        }

        // --- Jumps
        if (assists.HasBufferedJump && TryJump(ref vel, jumpPress, h))
        {
            assists.ConsumeJumpBuffer();
            gripping = false;
            gripAllowed = false;
        }
        else if (gripping)
        {
            assists.ConsumeJumpBuffer();
        }

        // --- Horizontal Movement
        if (wallJumpLockTimer <= 0f)
            vel.x = gripping ? 0f : ComputeHorizontal(vel.x, h, dt);

        // --- Vertical Movement, Gravity & Stress
        if (gripping)
        {
            rb.gravityScale = 0f;
            jumpRising = false;
            float v = input.Move.y;
            if (v > 0.1f)
            {
                vel.y = climbSpeed;
                if (stress != null) stress.ApplyWallClimbStress(dt);
            }
            else if (v < -0.1f)
            {
                vel.y = -slideSpeed;
                if (stress != null) stress.ApplyWallSlideStress(dt);
            }
            else
            {
                vel.y = 0f;
                if (stress != null) stress.ApplyWallHoldStress(dt);
            }
        }
        else
        {
            float scale = baseGravityScale;
            if (vel.y < 0f) scale *= fallGravityMultiplier;
            else if (vel.y > 0f && jumpRising && !input.JumpHeld) scale *= lowJumpGravityMultiplier;
            if (vel.y <= 0f) jumpRising = false;

            if (!grounded && Mathf.Abs(vel.y) < apexThreshold && input.JumpHeld)
                scale *= apexGravityMultiplier;

            rb.gravityScale = scale;
            vel.y = Mathf.Max(vel.y, -maxFallSpeed);

            if (grounded && groundIsSafe && stress != null)
                stress.RecoverOnSafeGround(dt);
        }

        // --- Corner correction on jump rising
        if (assists.CornerCorrectionOnJump && !gripping && vel.y > 0.1f)
            assists.CornerCorrect(rb, col, filter, vel, dt, pixelsPerUnit, false, true);

        rb.linearVelocity = vel;
    }

    private float ComputeHorizontal(float vx, float h, float dt)
    {
        float accelTime = grounded ? accelerationTime : airAccelerationTime;
        float decelTime = grounded ? decelerationTime : airDecelerationTime;

        if (h != 0f)
        {
            if (instantAirTurn && !grounded && Mathf.Abs(vx) > 0.01f && Mathf.Sign(vx) != h)
                vx = h * Mathf.Abs(vx);

            bool overSpeed = Mathf.Abs(vx) > maxSpeed && Mathf.Sign(vx) == h;
            float rate = maxSpeed / Mathf.Max(overSpeed ? decelTime : accelTime, 0.0001f);
            return Mathf.MoveTowards(vx, h * maxSpeed, rate * dt);
        }

        float decel = maxSpeed / Mathf.Max(decelTime, 0.0001f);
        return Mathf.MoveTowards(vx, 0f, decel * dt);
    }

    // ------------------------------------------------------------------ Jumping
    private bool TryJump(ref Vector2 vel, bool freshPress, float h)
    {
        // 1) Ground jump (with coyote time)
        if (assists.CanCoyoteJump)
        {
            vel.y = VelocityForHeight(jumpHeight);
            assists.ConsumeCoyote();
            jumpRising = true;
            return true;
        }

        // 2) Wall-jump
        if (assists.CanWallCoyoteJump && lastWallSide != 0 && !input.GripHeld
            && h == -lastWallSide && wallJumpsUsed < maxWallJumps)
        {
            vel = new Vector2(-lastWallSide * wallJumpSpeedX, VelocityForHeight(wallJumpHeight));
            facing = -lastWallSide;
            wallJumpsUsed++;
            assists.ConsumeWallCoyote();
            wallJumpLockTimer = wallJumpControlLock;
            jumpRising = true;
            if (stress != null) stress.ApplyWallJumpStress();
            return true;
        }

        // 3) Air Jump / Double Jump
        if (freshPress && airJumpsLeft > 0 && !gripping)
        {
            if (vel.y <= 0f && assists.IsGroundWithinLandingBuffer(col, filter, rb))
                return false;

            if (stress != null && stress.IsLiquidLocked)
            {
                stress.NotifyLiquidLockoutFail();
                assists.ConsumeJumpBuffer();
                return false;
            }

            vel.y = VelocityForHeight(airJumpHeight);
            airJumpsLeft--;
            jumpRising = true;
            if (stress != null) stress.ApplyDoubleJumpStress();
            return true;
        }

        return false;
    }

    private float VelocityForHeight(float height)
    {
        float g = Mathf.Abs(Physics2D.gravity.y) * baseGravityScale;
        return Mathf.Sqrt(2f * g * height);
    }

    // ------------------------------------------------------------------ Dash & Emergency Air Dash
    private void BeginDash(bool isEmergencyDash)
    {
        Vector2 dir = input.Move;
        if (dir == Vector2.zero) dir = new Vector2(facing, 0f);
        dashDir = dir.normalized;

        if (isEmergencyDash)
            canEmergencyDash = false;
        else
            dashesLeft--;

        state = MoveState.DashFreeze;
        dashFreezeTimer = dashFreezeDuration;

        gripping = false;
        jumpRising = false;
        assists.ConsumeCoyote();
        wallJumpLockTimer = 0f;

        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = 0f;

        if (stress != null)
        {
            if (isEmergencyDash)
                stress.ApplyEmergencyAirDashStress();
            else
                stress.ApplyNormalDashStress();
        }
    }

    private void TickDashFreeze()
    {
        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = 0f;

        dashFreezeTimer -= Time.fixedDeltaTime;
        if (dashFreezeTimer > 0f) return;

        if (resampleDirectionAfterFreeze && input.Move != Vector2.zero)
            dashDir = input.Move.normalized;

        state = MoveState.Dashing;
        dashTimer = dashDuration;
        rb.linearVelocity = dashDir * dashSpeed;
    }

    private void TickDashing(float dt)
    {
        rb.gravityScale = 0f;

        Vector2 v = dashDir * dashSpeed;
        assists.CornerCorrect(rb, col, filter, v, dt, pixelsPerUnit, true, true);
        rb.linearVelocity = v;

        dashTimer -= dt;
        if (dashTimer > 0f) return;

        state = MoveState.Normal;
        dashCooldownTimer = dashCooldown;
        rb.linearVelocity = dashDir * dashSpeed * dashExitSpeedMultiplier;
    }

    // ------------------------------------------------------------------ Environment Sensing & Timers
    private void SenseEnvironment(Vector2 vel)
    {
        Bounds b = col.bounds;
        Vector2 c = b.center;
        Vector2 e = b.extents;

        // Ground check
        Vector2 groundCenter = new Vector2(c.x, c.y - e.y - checkDistance * 0.5f);
        int n = Physics2D.OverlapBox(groundCenter, new Vector2(b.size.x * 0.9f, checkDistance), 0f, filter, hits);
        bool touchingGround = false;
        groundIsSafe = false;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].attachedRigidbody == rb) continue;
            touchingGround = true;
            if (hits[i].GetComponentInParent<FragilePlatform>() == null) groundIsSafe = true;
        }
        grounded = touchingGround && vel.y <= 0.1f;

        // Wall checks
        Vector2 wallSize = new Vector2(checkDistance, b.size.y * 0.5f);
        bool right = BoxHit(new Vector2(c.x + e.x + checkDistance * 0.5f, c.y), wallSize);
        bool left = BoxHit(new Vector2(c.x - e.x - checkDistance * 0.5f, c.y), wallSize);
        wallSide = (left && right) ? facing : right ? 1 : left ? -1 : 0;
    }

    private void TickTimers(float dt)
    {
        assists.Tick(dt, grounded, wallSide, ref lastWallSide);

        if (grounded)
        {
            airJumpsLeft = maxAirJumps;
            wallJumpsUsed = 0;
            if (state == MoveState.Normal && dashCooldownTimer <= 0f)
            {
                dashesLeft = maxDashes;
                canEmergencyDash = true;
            }
        }

        dashCooldownTimer = Mathf.Max(0f, dashCooldownTimer - dt);
        wallJumpLockTimer = Mathf.Max(0f, wallJumpLockTimer - dt);
    }

    private bool BoxHit(Vector2 center, Vector2 size)
    {
        int n = Physics2D.OverlapBox(center, size, 0f, filter, hits);
        for (int i = 0; i < n; i++)
            if (hits[i].attachedRigidbody != rb) return true;
        return false;
    }
}
