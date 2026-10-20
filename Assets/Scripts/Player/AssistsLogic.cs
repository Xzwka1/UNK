using UnityEngine;

/// <summary>
/// Encapsulates platformer assist mechanics (Coyote Time, Wall Coyote Time, Jump Buffering,
/// Air Jump Landing Buffer, and Corner Correction) to keep PlayerController2D clean and focused.
/// </summary>
[System.Serializable]
public class AssistsLogic
{
    // ------------------------------------------------------------------ Inspector Settings
    [Header("Coyote Time")]
    [Tooltip("Jump still allowed this long after leaving a ledge.")]
    [SerializeField, Range(0.08f, 0.12f)] private float coyoteTime = 0.1f;
    [Tooltip("Wall-jump still allowed this long after leaving a wall.")]
    [SerializeField] private float wallCoyoteTime = 0.08f;

    [Header("Jump Buffer")]
    [Tooltip("A jump pressed this long before landing fires on landing.")]
    [SerializeField] private float jumpBufferTime = 0.12f;
    [Tooltip("If the ground is closer than this while falling, a jump press is buffered for landing instead of burning the air jump.")]
    [SerializeField] private float airJumpLandingBufferDistance = 0.6f;

    [Header("Corner Correction")]
    [Tooltip("Max corner nudge in pixels (0 = off). Applied while dashing.")]
    [SerializeField, Range(0, 8)] private int cornerCorrectionPixels = 4;
    [Tooltip("Also nudge sideways when the head clips a ceiling corner during a jump.")]
    [SerializeField] private bool cornerCorrectionOnJump = true;

    // ------------------------------------------------------------------ Runtime State
    private float coyoteTimer;
    private float wallCoyoteTimer;
    private float jumpBufferTimer;

    private const float CornerSkin = 0.02f; // shrinks overlap tests so resting contact is not blocked
    private readonly Collider2D[] hits = new Collider2D[8];

    // ------------------------------------------------------------------ Properties
    public float CoyoteTime => coyoteTime;
    public float WallCoyoteTime => wallCoyoteTime;
    public float JumpBufferTime => jumpBufferTime;
    public float AirJumpLandingBufferDistance => airJumpLandingBufferDistance;
    public int CornerCorrectionPixels => cornerCorrectionPixels;
    public bool CornerCorrectionOnJump => cornerCorrectionOnJump;

    public bool HasBufferedJump => jumpBufferTimer > 0f;
    public bool CanCoyoteJump => coyoteTimer > 0f;
    public bool CanWallCoyoteJump => wallCoyoteTimer > 0f;

    // ------------------------------------------------------------------ Timers & Buffering API
    /// <summary>Ticks all assist timers by deltaTime.</summary>
    public void Tick(float dt, bool grounded, int wallSide, ref int lastWallSide)
    {
        if (grounded)
        {
            coyoteTimer = coyoteTime;
        }
        else
        {
            coyoteTimer = Mathf.Max(0f, coyoteTimer - dt);
        }

        if (wallSide != 0)
        {
            lastWallSide = wallSide;
            wallCoyoteTimer = wallCoyoteTime;
        }
        else
        {
            wallCoyoteTimer = Mathf.Max(0f, wallCoyoteTimer - dt);
        }

        jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - dt);
    }

    /// <summary>Buffers a jump input press for up to jumpBufferTime seconds.</summary>
    public void BufferJump() => jumpBufferTimer = jumpBufferTime;

    /// <summary>Consumes the buffered jump press so it doesn't fire twice.</summary>
    public void ConsumeJumpBuffer() => jumpBufferTimer = 0f;

    /// <summary>Consumes coyote time on jump or dash.</summary>
    public void ConsumeCoyote() => coyoteTimer = 0f;

    /// <summary>Consumes wall coyote time on wall-jump.</summary>
    public void ConsumeWallCoyote() => wallCoyoteTimer = 0f;

    /// <summary>Resets all assist timers to zero.</summary>
    public void ResetTimers()
    {
        coyoteTimer = 0f;
        wallCoyoteTimer = 0f;
        jumpBufferTimer = 0f;
    }

    // ------------------------------------------------------------------ Air Jump Landing Buffer
    /// <summary>
    /// Checks whether the ground is within airJumpLandingBufferDistance below the player.
    /// Used to preserve an air jump when the player is about to touch the ground.
    /// </summary>
    public bool IsGroundWithinLandingBuffer(CapsuleCollider2D col, ContactFilter2D filter, Rigidbody2D rb)
    {
        if (airJumpLandingBufferDistance <= 0f || col == null) return false;

        Bounds b = col.bounds;
        Vector2 center = new Vector2(b.center.x, b.min.y - airJumpLandingBufferDistance * 0.5f);
        Vector2 size = new Vector2(b.size.x * 0.9f, airJumpLandingBufferDistance);

        int n = Physics2D.OverlapBox(center, size, 0f, filter, hits);
        for (int i = 0; i < n; i++)
        {
            if (hits[i].attachedRigidbody != rb) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ Corner Correction
    /// <summary>
    /// If the next step would be blocked by a corner that overlaps by 1..cornerCorrectionPixels pixels,
    /// nudges the body sideways or vertically so it slips past cleanly without losing speed.
    /// </summary>
    public void CornerCorrect(Rigidbody2D rb, CapsuleCollider2D col, ContactFilter2D filter, Vector2 vel, float dt, float pixelsPerUnit, bool horizontal, bool vertical)
    {
        if (cornerCorrectionPixels <= 0 || rb == null || col == null) return;

        float px = 1f / Mathf.Max(1f, pixelsPerUnit);
        Vector2 pos = rb.position;
        Vector2 step = vel * dt;

        if (horizontal && Mathf.Abs(step.x) > 0.0001f)
        {
            Vector2 move = new Vector2(step.x, 0f);
            if (BlockedAt(rb, col, filter, pos + move) && TryNudge(rb, col, filter, pos, move, Vector2.up, px))
                return;
        }

        if (vertical && Mathf.Abs(step.y) > 0.0001f)
        {
            Vector2 move = new Vector2(0f, step.y);
            if (BlockedAt(rb, col, filter, pos + move))
                TryNudge(rb, col, filter, pos, move, Vector2.right, px);
        }
    }

    private bool TryNudge(Rigidbody2D rb, CapsuleCollider2D col, ContactFilter2D filter, Vector2 pos, Vector2 move, Vector2 axis, float px)
    {
        for (int k = 1; k <= cornerCorrectionPixels; k++)
        {
            for (int s = 1; s >= -1; s -= 2)
            {
                Vector2 offset = axis * (s * k * px);
                if (!BlockedAt(rb, col, filter, pos + offset) && !BlockedAt(rb, col, filter, pos + offset + move))
                {
                    rb.position = pos + offset;
                    return true;
                }
            }
        }
        return false;
    }

    private bool BlockedAt(Rigidbody2D rb, CapsuleCollider2D col, ContactFilter2D filter, Vector2 bodyPosition)
    {
        Vector3 s = col.transform.lossyScale;
        Vector2 size = new Vector2(col.size.x * Mathf.Abs(s.x), col.size.y * Mathf.Abs(s.y)) - Vector2.one * CornerSkin;
        Vector2 center = bodyPosition + new Vector2(col.offset.x * s.x, col.offset.y * s.y);

        int n = Physics2D.OverlapCapsule(center, size, col.direction, 0f, filter, hits);
        for (int i = 0; i < n; i++)
        {
            if (hits[i].attachedRigidbody != rb) return true;
        }
        return false;
    }
}
