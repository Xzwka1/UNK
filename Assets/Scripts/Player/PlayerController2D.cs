using UnityEngine;

/// <summary>
/// Precision-platformer player controller (Rigidbody2D, velocity driven) with
/// "The Fragile Equilibrium" Dual-Polarity Stress System (-100% to +100%).
///
/// Preserves 100% of core movement:
///   - Base horizontal speed, acceleration, deceleration, hitbox
///   - Variable jump height & apex hang
///   - Control assists: Coyote Time, Jump Buffering, Corner Correction
///   - Fast respawn integration
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D), typeof(PlayerStress))]
public class PlayerController2D : MonoBehaviour
{
    public enum MoveState { Normal, DashFreeze, Dashing }

    // ------------------------------------------------------------------ Inspector: Movement & Physics
    [Header("Run (world units / second)")]
    [SerializeField] private float maxSpeed = 9f;
    [Tooltip("Seconds from 0 to max speed on the ground.")]
    [SerializeField] private float accelerationTime = 0.08f;
    [Tooltip("Seconds from max speed to a full stop on the ground.")]
    [SerializeField] private float decelerationTime = 0.06f;
    [Tooltip("Reversing direction in mid-air flips the speed instantly instead of braking first.")]
    [SerializeField] private bool instantAirTurn = true;
    [Tooltip("Seconds from 0 to max speed while airborne.")]
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

    [Header("Jump & Double-Jump")]
    [Tooltip("Height reached when the jump button is held for the whole ascent.")]
    [SerializeField] private float jumpHeight = 3.2f;
    [Tooltip("Double-Jump (Air Jump) height.")]
    [SerializeField] private float airJumpHeight = 2.5f;

    [Header("Dash & Emergency Air Dash (Shift + WASD)")]
    [SerializeField] private float dashSpeed = 24f;
    [SerializeField] private float dashDuration = 0.15f;
    [Tooltip("Freeze frame right before the dash force is applied (0.025 - 0.05 s).")]
    [SerializeField, Range(0.025f, 0.05f)] private float dashFreezeDuration = 0.05f;
    [SerializeField] private float dashCooldown = 0.2f;
    [Tooltip("Fraction of the dash speed kept after the dash ends.")]
    [SerializeField, Range(0f, 1f)] private float dashExitSpeedMultiplier = 0.6f;
    [Tooltip("Re-read WASD when the freeze ends so slightly late diagonals still count.")]
    [SerializeField] private bool resampleDirectionAfterFreeze = true;
    [Tooltip("Allow a 2nd Emergency Air Dash once per airtime at a high stress cost (+45f).")]
    [SerializeField] private bool allowEmergencyAirDash = true;

    [Header("Wall (hold Ctrl to grip)")]
    [SerializeField] private float climbSpeed = 3f;
    [SerializeField] private float slideSpeed = 4f;
    [SerializeField] private float wallJumpSpeedX = 9f;
    [SerializeField] private float wallJumpHeight = 2.5f;
    [Tooltip("Horizontal input is ignored this long after a wall-jump so the push-off is felt.")]
    [SerializeField] private float wallJumpControlLock = 0.12f;
    [SerializeField] private int maxWallJumps = 5;
    [Tooltip("Cooldown between OnBrittleWallSlip / OnLiquidLockoutFail feedback triggers while holding an action.")]
    [SerializeField] private float lockoutFeedbackCooldown = 0.35f;

<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
    [Header("Control assists")]
    [SerializeField] private AssistsLogic assists = new AssistsLogic();
=======
    [Header("Impact Threshold")]
    [Tooltip("Downward speed (units/s) at or above which a landing counts as a Heavy Landing (-15f stress).")]
    [SerializeField] private float heavyLandingVelocityThreshold = 16f;

    [Header("Control Assists")]
    [Tooltip("Jump still allowed this long after leaving a ledge.")]
    [SerializeField, Range(0.08f, 0.12f)] private float coyoteTime = 0.1f;
    [Tooltip("Wall-jump still allowed this long after leaving a wall.")]
    [SerializeField] private float wallCoyoteTime = 0.08f;
    [Tooltip("A jump pressed this long before landing fires on landing.")]
    [SerializeField] private float jumpBufferTime = 0.12f;
    [Tooltip("If the ground is closer than this while falling, a jump press is buffered for landing instead of burning the air jump.")]
    [SerializeField] private float airJumpLandingBufferDistance = 0.6f;
    [Tooltip("Max corner nudge in pixels (0 = off). Applied while dashing.")]
    [SerializeField, Range(0, 8)] private int cornerCorrectionPixels = 4;
    [Tooltip("Also nudge sideways when the head clips a ceiling corner during a jump.")]
    [SerializeField] private bool cornerCorrectionOnJump = true;
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs

    [Header("Environment Checks")]
    [Tooltip("Layers that count as ground / walls. Triggers are always ignored.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float checkDistance = 0.06f;
    [Tooltip("Must match the art scale (used to convert corner-correction pixels to world units).")]
    [SerializeField] private float pixelsPerUnit = 16f;

    // ------------------------------------------------------------------ Runtime State
    private Rigidbody2D rb;
    private CapsuleCollider2D col;
    private PlayerStress stress;

    private ContactFilter2D filter;
    private readonly Collider2D[] hits = new Collider2D[8];
<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
=======
    private const float CornerSkin = 0.02f;
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs

    private MoveState state = MoveState.Normal;
    private bool controlEnabled = true;

    private PlayerInputState input;
    private bool jumpPressLatch, dashPressLatch;

<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
    private bool grounded, gripping, jumpRising, gripAllowed = true;
=======
    private bool grounded, groundIsSafe, gripping, jumpRising;
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs
    private int wallSide, lastWallSide, facing = 1;

    private float dashFreezeTimer, dashTimer, dashCooldownTimer, wallJumpLockTimer;
    private float lockoutFeedbackTimer;

    // Quotas (exposed for Polarity Snap & BreakingBarrier refills)
    private bool canDash = true;
    private bool canEmergencyDash = true;
    private bool canDoubleJump = true;
    private int wallJumpsUsed;

    private Vector2 dashDir;
    private float lastVelocityY;

    // ------------------------------------------------------------------ Public Read-Only Info
    public bool IsGrounded => grounded;
    public bool IsGripping => gripping;
    public bool IsDashing => state != MoveState.Normal;
    public bool CanDash => canDash;
    public bool CanDoubleJump => canDoubleJump;
    public int WallSide => wallSide;
    public int Facing => facing;
    public MoveState State => state;
<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
    public AssistsLogic Assists => assists;
=======
    public StressStage Stage => stress != null ? stress.Stage : StressStage.Stable;
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs

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
        if (stress == null) stress = GetComponent<PlayerStress>();
        if (stress != null) stress.PolaritySnapped += OnPolaritySnapBonus;
    }

    private void OnDisable()
    {
        if (stress != null) stress.PolaritySnapped -= OnPolaritySnapBonus;
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

        if (controlEnabled) lastVelocityY = rb.linearVelocity.y;
    }

    // ------------------------------------------------------------------ Public Control & Refill API
    public void SetControlEnabled(bool enabled)
    {
        controlEnabled = enabled;
        if (!enabled)
        {
            jumpPressLatch = dashPressLatch = false;
            rb.linearVelocity = Vector2.zero;
        }
    }

    /// <summary>Refills 1 Air Dash immediately (called by BreakingBarrier impact and Polarity Snap).</summary>
    public void RefundAirDash()
    {
        canDash = true;
    }

    /// <summary>Callback when Polarity Snap Bonus triggers: refills both canDash and canDoubleJump mid-air.</summary>
    private void OnPolaritySnapBonus()
    {
        canDash = true;
        canDoubleJump = true;
    }

    /// <summary>Clears every timer / counter and returns the player to a clean idle state.</summary>
    public void ResetState()
    {
        state = MoveState.Normal;
        assists.ResetTimers();
        dashFreezeTimer = dashTimer = dashCooldownTimer = wallJumpLockTimer = 0f;
        lockoutFeedbackTimer = 0f;
        canDash = true;
        canEmergencyDash = true;
        canDoubleJump = true;
        wallJumpsUsed = 0;
        gripping = jumpRising = false;
<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
        gripAllowed = true;
=======
        lastVelocityY = 0f;
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs
        jumpPressLatch = dashPressLatch = false;
        input = default;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = baseGravityScale;
        }
    }

    // ------------------------------------------------------------------ Collisions: BreakingBarrier & Heavy Landing
    private void OnCollisionEnter2D(Collision2D c)
    {
        if (!controlEnabled || stress == null || stress.IsOverloaded) return;

        // 1) Breaking Barrier Impact while Dashing: destroy barrier, -30f stress, refund 1 Air Dash.
        if (state == MoveState.Dashing)
        {
            var barrier = c.collider.GetComponentInParent<BreakingBarrier>();
            if (barrier != null)
            {
                barrier.Break(this);
                return;
            }
        }

        // 2) Heavy Landing: falling onto solid ground at or faster than heavyLandingVelocityThreshold -> -15f stress.
        if (c.contactCount > 0 && c.GetContact(0).normal.y > 0.5f && lastVelocityY <= -heavyLandingVelocityThreshold)
        {
            stress.ApplyHeavyLandingStress();
        }
    }

    // ------------------------------------------------------------------ Normal Movement & Lockout Rules
    private void TickNormal(Vector2 vel, float dt, bool jumpPress, bool dashPress)
    {
        float h = Mathf.Abs(input.Move.x) > 0.1f ? Mathf.Sign(input.Move.x) : 0f;
        if (h != 0f) facing = (int)h;
        lockoutFeedbackTimer = Mathf.Max(0f, lockoutFeedbackTimer - dt);

        // --- Dash & Emergency Air Dash (with +80% Critical Meltdown Lockout) ---
        if (dashPress && dashCooldownTimer <= 0f)
        {
            if (stress.IsLiquidLocked)
            {
                // Critical Meltdown (+80% to +99.9%): cannot Dash or Emergency Dash!
                stress.NotifyLiquidLockoutFail();
            }
            else if (canDash)
            {
                BeginDash(isEmergencyDash: false);
                return;
            }
            else if (!grounded && allowEmergencyAirDash && canEmergencyDash)
            {
                BeginDash(isEmergencyDash: true);
                return;
            }
        }

<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
        // --- Grip eligibility:
        // Releasing Space allows gripping.
        // Holding Space while not touching a wall (or after jumping) disallows auto-grip upon touching a wall.
        if (!input.GripHeld)
        {
            gripAllowed = true;
        }
        else if (wallSide == 0)
        {
            gripAllowed = false;
        }

        // --- Grip (Space held + grip allowed + touching a wall)
        gripping = input.GripHeld && gripAllowed && wallSide != 0 && wallJumpLockTimer <= 0f;

        // --- Jumps (ground / wall / air). The buffer lets a press fire up to jumpBufferTime later.
        if (assists.HasBufferedJump && TryJump(ref vel, jumpPress, h))
=======
        // --- Wall Grip & Critical Crystallization Lockout (<= -80%) ---
        if (wallSide != 0 && stress.IsBrittleLocked)
        {
            // Critical Crystallization (-80% to -99.9%): 0% grip! Cancel grip and fall like a stone.
            gripping = false;
            if (lockoutFeedbackTimer <= 0f)
            {
                stress.NotifyBrittleWallSlip();
                lockoutFeedbackTimer = lockoutFeedbackCooldown;
            }
        }
        else
        {
            gripping = input.GripHeld && wallSide != 0 && wallJumpLockTimer <= 0f;
        }

        // --- Jumps (Ground / Wall-Jump / Double-Jump) ---
        if (jumpBufferTimer > 0f && TryJump(ref vel, jumpPress, h))
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs
        {
            assists.ConsumeJumpBuffer();
            gripping = false;
            gripAllowed = false; // Jumping off; holding Space is for jump ascent, not wall-grip
        }
        else if (gripping)
        {
            assists.ConsumeJumpBuffer(); // Consumed by grip, don't keep buffered
        }
        if (!controlEnabled) return; // in case jump/wall-jump stress triggered ±100% overload

        // --- Horizontal ---
        if (wallJumpLockTimer <= 0f)
            vel.x = gripping ? 0f : ComputeHorizontal(vel.x, h, dt);

        // --- Vertical / Gravity / Wall Actions & Safe Ground Recovery ---
        if (gripping)
        {
            rb.gravityScale = 0f;
            jumpRising = false;
            vel.y = ComputeGripVertical(dt);
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

            // Safe Ground Recovery: pull currentStress toward 0f at 10f/sec (not on FragilePlatform).
            if (grounded && groundIsSafe)
            {
                stress.RecoverOnSafeGround(dt);
            }
        }
        if (!controlEnabled) return;

<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
        // --- Head clipping a ceiling corner while rising
        if (assists.CornerCorrectionOnJump && !gripping && vel.y > 0.1f)
            assists.CornerCorrect(rb, col, filter, vel, dt, pixelsPerUnit, false, true);
=======
        // --- Head clipping a ceiling corner while rising ---
        if (cornerCorrectionOnJump && !gripping && vel.y > 0.1f)
            CornerCorrect(vel, dt, false, true);
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs

        rb.linearVelocity = vel;
    }

    /// <summary>
    /// Wall vertical velocity & Dual-Polarity Stress modifiers:
    ///   - Ctrl + W (Climb Up): +30f/sec. Locked out when stress >= +80f (Critical Meltdown).
    ///   - Ctrl + S (Slide Down): -35f/sec.
    ///   - Ctrl only (Hold Still): -25f/sec.
    /// </summary>
    private float ComputeGripVertical(float dt)
    {
        float v = input.Move.y;

        if (v > 0f)
        {
            // Critical Meltdown (+80% to +99.9%): Wall-Climb Up is strictly locked out!
            if (stress.IsLiquidLocked)
            {
                if (lockoutFeedbackTimer <= 0f)
                {
                    stress.NotifyLiquidLockoutFail();
                    lockoutFeedbackTimer = lockoutFeedbackCooldown;
                }
                // Cannot climb up, but can still hold still (-25f/s) to cool down below +80%
                stress.ApplyWallHoldStress(dt);
                return 0f;
            }

            stress.ApplyWallClimbStress(dt); // +30f / sec
            return climbSpeed;
        }

        if (v < 0f)
        {
            stress.ApplyWallSlideStress(dt); // -35f / sec
            return -slideSpeed;
        }

        stress.ApplyWallHoldStress(dt);      // -25f / sec
        return 0f;
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
<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
        // 1) Ground jump (includes coyote time).
        if (assists.CanCoyoteJump)
=======
        // 1) Ground jump (includes Coyote Time) — free of stress.
        if (coyoteTimer > 0f)
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs
        {
            vel.y = VelocityForHeight(jumpHeight);
            assists.ConsumeCoyote();
            jumpRising = true;
            return true;
        }

<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
        // 2) Wall-jump: Ctrl released + direction AWAY from the wall + jump.
        if (assists.CanWallCoyoteJump && lastWallSide != 0 && !input.GripHeld
=======
        // 2) Wall-Jump: Ctrl released + direction away from wall + jump.
        //    Locked out in Critical Crystallization (<= -80%). Allowed in Critical Meltdown (>= +80%) as an escape!
        if (wallCoyoteTimer > 0f && lastWallSide != 0 && !input.GripHeld
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs
            && h == -lastWallSide && wallJumpsUsed < maxWallJumps)
        {
            if (stress.IsBrittleLocked)
            {
                stress.NotifyBrittleWallSlip();
                return false;
            }

            vel = new Vector2(-lastWallSide * wallJumpSpeedX, VelocityForHeight(wallJumpHeight));
            facing = -lastWallSide;
            wallJumpsUsed++;
            assists.ConsumeWallCoyote();
            wallJumpLockTimer = wallJumpControlLock;
            jumpRising = true;
            stress.ApplyWallJumpStress(); // -20f instant
            return true;
        }

        // 3) Double-Jump (Air Jump): only on fresh press, not while gripping.
        //    Locked out in Critical Meltdown (>= +80%).
        if (freshPress && canDoubleJump && !gripping)
        {
<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
            // Landing soon? Keep the press buffered for a ground jump instead of wasting the air jump.
            if (vel.y <= 0f && assists.IsGroundWithinLandingBuffer(col, filter, rb)) return false;
=======
            if (vel.y <= 0f && GroundWithin(airJumpLandingBufferDistance)) return false;
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs

            if (stress.IsLiquidLocked)
            {
                stress.NotifyLiquidLockoutFail();
                jumpBufferTimer = 0f;
                return false;
            }

            vel.y = VelocityForHeight(airJumpHeight);
            canDoubleJump = false;
            jumpRising = true;
            stress.ApplyDoubleJumpStress(); // +20f instant
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
            canDash = false;

        state = MoveState.DashFreeze;
        dashFreezeTimer = dashFreezeDuration;

        gripping = false;
        jumpRising = false;
        assists.ConsumeCoyote();
        wallJumpLockTimer = 0f;

        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = 0f;

        // Apply stress: Emergency Dash (+45f) or Normal Dash (+25f, or +35f Thermal Shock if < 0f)
        // Note: if this triggers Polarity Snap Bonus, OnPolaritySnapBonus() will refill canDash & canDoubleJump!
        if (isEmergencyDash)
            stress.ApplyEmergencyAirDashStress();
        else
            stress.ApplyNormalDashStress();
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
<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
        assists.CornerCorrect(rb, col, filter, v, dt, pixelsPerUnit, true, true);
        rb.linearVelocity = v; // re-applied each step so collisions never bleed the dash speed
=======
        CornerCorrect(v, dt, true, true);
        rb.linearVelocity = v;
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs

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
<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
            airJumpsLeft = maxAirJumps;   // refills ONLY on the ground
=======
            coyoteTimer = coyoteTime;
            canDoubleJump = true;
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs
            wallJumpsUsed = 0;
            if (state == MoveState.Normal && dashCooldownTimer <= 0f)
            {
                canDash = true;
                canEmergencyDash = true;
            }
        }

<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
=======
        if (wallSide != 0 && !stress.IsBrittleLocked)
        {
            lastWallSide = wallSide;
            wallCoyoteTimer = wallCoyoteTime;
        }
        else
        {
            wallCoyoteTimer = Mathf.Max(0f, wallCoyoteTimer - dt);
        }

        jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - dt);
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs
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

<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
    public void RefillDash()
=======
    // ------------------------------------------------------------------ Corner Correction
    private void CornerCorrect(Vector2 vel, float dt, bool horizontal, bool vertical)
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs
    {
        dashesLeft = maxDashes;
        dashCooldownTimer = 0f;
        if (state == MoveState.Dashing || state == MoveState.DashFreeze)
        {
            state = MoveState.Normal;
        }
<<<<<<< HEAD:Assets/Scripts/Player/PlayerController2D.cs
=======

        if (vertical && Mathf.Abs(step.y) > 0.0001f)
        {
            Vector2 move = new Vector2(0f, step.y);
            if (BlockedAt(pos + move)) TryNudge(pos, move, Vector2.right, px);
        }
    }

    private bool TryNudge(Vector2 pos, Vector2 move, Vector2 axis, float px)
    {
        for (int k = 1; k <= cornerCorrectionPixels; k++)
        {
            for (int s = 1; s >= -1; s -= 2)
            {
                Vector2 offset = axis * (s * k * px);
                if (!BlockedAt(pos + offset) && !BlockedAt(pos + offset + move))
                {
                    rb.position = pos + offset;
                    return true;
                }
            }
        }
        return false;
    }

    private bool BlockedAt(Vector2 bodyPosition)
    {
        Vector3 s = transform.lossyScale;
        Vector2 size = new Vector2(col.size.x * Mathf.Abs(s.x), col.size.y * Mathf.Abs(s.y)) - Vector2.one * CornerSkin;
        Vector2 center = bodyPosition + new Vector2(col.offset.x * s.x, col.offset.y * s.y);

        int n = Physics2D.OverlapCapsule(center, size, col.direction, 0f, filter, hits);
        for (int i = 0; i < n; i++)
            if (hits[i].attachedRigidbody != rb) return true;
        return false;
>>>>>>> Earth_Prototype_test:Assets/Scripts/Greybox/PlayerController2D.cs
    }
}
