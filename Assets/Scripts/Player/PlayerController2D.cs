using UnityEngine;

/// <summary>
/// Precision-platformer player controller (Rigidbody2D, velocity driven).
///
/// Features: variable jump, 1 air jump (ground-reset only), 8-direction dash with freeze frame,
/// Ctrl wall grip / climb / slide, wall-jump, stress feed, coyote time, jump buffering, corner correction.
///
/// Input is sampled in Update and consumed in FixedUpdate (presses are latched so none are lost).
/// NOTE: timers tick in fixed steps, so durations such as the dash freeze are quantised to Time.fixedDeltaTime
/// (0.02 s by default). Lower the fixed timestep (e.g. 0.01) for finer control.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D), typeof(PlayerStress))]
public class PlayerController2D : MonoBehaviour
{
    public enum MoveState { Normal, DashFreeze, Dashing }

    // ------------------------------------------------------------------ Inspector
    [Header("Run (world units / second)")]
    [SerializeField] private float maxSpeed = 9f;
    [Tooltip("Seconds from 0 to max speed.")]
    [SerializeField] private float accelerationTime = 0.08f;
    [Tooltip("Seconds from max speed to a full stop (high friction, no sliding).")]
    [SerializeField] private float decelerationTime = 0.06f;
    [Tooltip("Reversing direction in mid-air flips the speed instantly instead of braking first.")]
    [SerializeField] private bool instantAirTurn = true;
    [Tooltip("Seconds from 0 to max speed while airborne (Celeste-style: slightly softer than the ground).")]
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
    [Tooltip("Air jumps per airtime. Refilled ONLY on the ground (never on walls).")]
    [SerializeField] private int maxAirJumps = 1;

    [Header("Dash (Shift + WASD)")]
    [SerializeField] private float dashSpeed = 24f;
    [SerializeField] private float dashDuration = 0.15f;
    [Tooltip("Freeze frame right before the dash force is applied (0.025 - 0.05 s).")]
    [SerializeField, Range(0.025f, 0.05f)] private float dashFreezeDuration = 0.05f;
    [Tooltip("Dashes per airtime. Refilled on the ground.")]
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

    [Header("Control assists")]
    [SerializeField] private AssistsLogic assists = new AssistsLogic();

    [Header("Environment checks")]
    [Tooltip("Layers that count as ground / walls. Triggers are always ignored.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float checkDistance = 0.06f;
    [Tooltip("Must match the art scale (used to convert corner-correction pixels to world units).")]
    [SerializeField] private float pixelsPerUnit = 16f;

    // ------------------------------------------------------------------ State
    private Rigidbody2D rb;
    private CapsuleCollider2D col;
    private PlayerStress stress;

    private ContactFilter2D filter;
    private readonly Collider2D[] hits = new Collider2D[8];

    private MoveState state = MoveState.Normal;
    private bool controlEnabled = true;

    private PlayerInputState input;
    private bool jumpPressLatch, dashPressLatch;

    private bool grounded, gripping, jumpRising;
    private int wallSide, lastWallSide, facing = 1;

    private float dashFreezeTimer, dashTimer, dashCooldownTimer, wallJumpLockTimer;
    private int airJumpsLeft, dashesLeft, wallJumpsUsed;
    private Vector2 dashDir;

    // ------------------------------------------------------------------ Public read-only info
    public bool IsGrounded => grounded;
    public bool IsGripping => gripping;
    public int WallSide => wallSide;
    public int Facing => facing;
    public MoveState State => state;
    public AssistsLogic Assists => assists;

    // ------------------------------------------------------------------ Lifecycle
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CapsuleCollider2D>();
        stress = GetComponent<PlayerStress>();
        ResetState();
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
    }

    // ------------------------------------------------------------------ Public control API (used by PlayerRespawn)
    public void SetControlEnabled(bool enabled)
    {
        controlEnabled = enabled;
        if (!enabled)
        {
            jumpPressLatch = dashPressLatch = false;
            rb.linearVelocity = Vector2.zero;
        }
    }

    /// <summary>Clears every timer / counter and returns the player to a clean idle state.</summary>
    public void ResetState()
    {
        state = MoveState.Normal;
        assists.ResetTimers();
        dashFreezeTimer = dashTimer = dashCooldownTimer = wallJumpLockTimer = 0f;
        airJumpsLeft = maxAirJumps;
        dashesLeft = maxDashes;
        wallJumpsUsed = 0;
        gripping = jumpRising = false;
        jumpPressLatch = dashPressLatch = false;
        input = default;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = baseGravityScale;
        }
    }

    // ------------------------------------------------------------------ Normal movement
    private void TickNormal(Vector2 vel, float dt, bool jumpPress, bool dashPress)
    {
        float h = Mathf.Abs(input.Move.x) > 0.1f ? Mathf.Sign(input.Move.x) : 0f;
        if (h != 0f) facing = (int)h;

        // --- Dash
        if (dashPress && dashesLeft > 0 && dashCooldownTimer <= 0f)
        {
            BeginDash();
            return;
        }

        // --- Grip (Ctrl + touching a wall)
        gripping = input.GripHeld && wallSide != 0 && wallJumpLockTimer <= 0f;

        // --- Jumps (ground / wall / air). The buffer lets a press fire up to jumpBufferTime later.
        if (assists.HasBufferedJump && TryJump(ref vel, jumpPress, h))
        {
            assists.ConsumeJumpBuffer();
            gripping = false;
        }

        // --- Horizontal
        if (wallJumpLockTimer <= 0f)
            vel.x = gripping ? 0f : ComputeHorizontal(vel.x, h, dt);

        // --- Vertical / gravity / stress
        if (gripping)
        {
            rb.gravityScale = 0f;
            jumpRising = false;
            float v = input.Move.y;
            if (v > 0f)
            {
                vel.y = climbSpeed;
                stress.AddRate(stress.climbUpRate, dt);
            }
            else if (v < 0f)
            {
                vel.y = -slideSpeed;
                stress.AddRate(stress.slideDownRate, dt);
            }
            else
            {
                vel.y = 0f;
                stress.AddRate(stress.gripHoldRate, dt);
            }
        }
        else
        {
            float scale = baseGravityScale;
            if (vel.y < 0f) scale *= fallGravityMultiplier;
            else if (vel.y > 0f && jumpRising && !input.JumpHeld) scale *= lowJumpGravityMultiplier;
            if (vel.y <= 0f) jumpRising = false;

            if (!grounded && Mathf.Abs(vel.y) < apexThreshold && input.JumpHeld)
                scale *= apexGravityMultiplier; // floaty apex

            rb.gravityScale = scale;
            vel.y = Mathf.Max(vel.y, -maxFallSpeed);

            if (grounded) stress.Recover(dt); // safe ground: -40 %/s
        }

        // --- Head clipping a ceiling corner while rising
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

            // Over max speed (e.g. after a dash): shed it gently instead of snapping to maxSpeed.
            bool overSpeed = Mathf.Abs(vx) > maxSpeed && Mathf.Sign(vx) == h;
            float rate = maxSpeed / Mathf.Max(overSpeed ? decelTime : accelTime, 0.0001f);
            return Mathf.MoveTowards(vx, h * maxSpeed, rate * dt);
        }

        float decel = maxSpeed / Mathf.Max(decelTime, 0.0001f);
        return Mathf.MoveTowards(vx, 0f, decel * dt);
    }

    // ------------------------------------------------------------------ Jumping
    /// <returns>True if a jump was performed.</returns>
    private bool TryJump(ref Vector2 vel, bool freshPress, float h)
    {
        // 1) Ground jump (includes coyote time).
        if (assists.CanCoyoteJump)
        {
            vel.y = VelocityForHeight(jumpHeight);
            assists.ConsumeCoyote();
            jumpRising = true;
            return true;
        }

        // 2) Wall-jump: Ctrl released + direction AWAY from the wall + jump.
        if (assists.CanWallCoyoteJump && lastWallSide != 0 && !input.GripHeld
            && h == -lastWallSide && wallJumpsUsed < maxWallJumps)
        {
            vel = new Vector2(-lastWallSide * wallJumpSpeedX, VelocityForHeight(wallJumpHeight));
            facing = -lastWallSide;
            wallJumpsUsed++;
            assists.ConsumeWallCoyote();
            wallJumpLockTimer = wallJumpControlLock;
            jumpRising = true;
            stress.Add(stress.wallJumpCost); // +20 % per wall-jump
            return true;
        }

        // 3) Air jump: only on the actual press (never from the buffer), not while gripping.
        if (freshPress && airJumpsLeft > 0 && !gripping)
        {
            // Landing soon? Keep the press buffered for a ground jump instead of wasting the air jump.
            if (vel.y <= 0f && assists.IsGroundWithinLandingBuffer(col, filter, rb)) return false;

            vel.y = VelocityForHeight(airJumpHeight);
            airJumpsLeft--;
            jumpRising = true;
            return true;
        }

        return false;
    }

    private float VelocityForHeight(float height)
    {
        float g = Mathf.Abs(Physics2D.gravity.y) * baseGravityScale;
        return Mathf.Sqrt(2f * g * height);
    }

    // ------------------------------------------------------------------ Dash
    private void BeginDash()
    {
        Vector2 dir = input.Move;
        if (dir == Vector2.zero) dir = new Vector2(facing, 0f);
        dashDir = dir.normalized;

        dashesLeft--;
        state = MoveState.DashFreeze;
        dashFreezeTimer = dashFreezeDuration;

        gripping = false;
        jumpRising = false;
        assists.ConsumeCoyote();
        wallJumpLockTimer = 0f;

        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = 0f;
    }

    private void TickDashFreeze()
    {
        // Hit-stop: hold the player perfectly still.
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
        rb.linearVelocity = v; // re-applied each step so collisions never bleed the dash speed

        dashTimer -= dt;
        if (dashTimer > 0f) return;

        state = MoveState.Normal;
        dashCooldownTimer = dashCooldown;
        rb.linearVelocity = dashDir * dashSpeed * dashExitSpeedMultiplier;
    }

    // ------------------------------------------------------------------ Environment sensing & timers
    private void SenseEnvironment(Vector2 vel)
    {
        Bounds b = col.bounds;
        Vector2 c = b.center;
        Vector2 e = b.extents;

        // Ground: thin box under the feet.
        Vector2 groundCenter = new Vector2(c.x, c.y - e.y - checkDistance * 0.5f);
        bool touchingGround = BoxHit(groundCenter, new Vector2(b.size.x * 0.9f, checkDistance));
        grounded = touchingGround && vel.y <= 0.1f; // rising = not grounded (prevents refills right after a jump)

        // Walls: thin boxes beside the straight part of the capsule.
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
            airJumpsLeft = maxAirJumps;   // refills ONLY on the ground
            wallJumpsUsed = 0;
            if (state == MoveState.Normal && dashCooldownTimer <= 0f) dashesLeft = maxDashes;
        }

        dashCooldownTimer = Mathf.Max(0f, dashCooldownTimer - dt);
        wallJumpLockTimer = Mathf.Max(0f, wallJumpLockTimer - dt);
    }

    private bool BoxHit(Vector2 center, Vector2 size)
    {
        int n = Physics2D.OverlapBox(center, size, 0f, filter, hits);
        for (int i = 0; i < n; i++)
            if (hits[i].attachedRigidbody != rb) return true; // ignore our own collider
        return false;
    }
}
