using UnityEngine;

/// <summary>
/// Side-scrolling follow camera for a precision platformer (orthographic camera, no Cinemachine needed).
///
/// Each LateUpdate:
///   1. Lookahead   - a horizontal offset that eases towards the direction the player is moving.
///   2. Deadzone    - the "focus" point only moves when the target pushes against the edge of a small box.
///                    Inside the box the camera does NOT move (small jumps, minor adjustments).
///   3. Room bounds - the desired camera centre is confined so the VIEW never leaves the room rectangle.
///                    A room smaller than the view on an axis becomes a static room on that axis (camera centred).
///   4. Damping     - Mathf.SmoothDamp towards the desired position (separate X / Y smooth times).
///   5. Shake       - the offset from CameraEffects (if present) is added on top, never fed back into the follow logic.
///
/// Setup: add this script to the Main Camera (it auto-adds a Camera requirement), drag the Player into 'Target'
/// (or leave it empty and the first PlayerController2D in the scene is used). Set the player's Rigidbody2D
/// Interpolation to 'Interpolate' so the camera does not stutter against the 50 Hz physics step.
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)]
public class CameraController2D : MonoBehaviour
{
    // ------------------------------------------------------------------ Inspector
    [Header("Target")]
    [Tooltip("The Player. Empty = first PlayerController2D found in the scene.")]
    [SerializeField] private Transform target;
    [Tooltip("Jump the camera straight to the player when they respawn (instead of gliding across the level).")]
    [SerializeField] private bool snapOnRespawn = true;

    [Header("Smooth damping (seconds, lower = tighter)")]
    [SerializeField, Min(0f)] private float smoothTimeX = 0.12f;
    [Tooltip("Vertical is usually softer so jumps do not make the view bob.")]
    [SerializeField, Min(0f)] private float smoothTimeY = 0.18f;

    [Header("Box deadzone (world units, centred on the view)")]
    [Tooltip("Width / Height of the box. The camera only moves when the target pushes past an edge of this box.")]
    [SerializeField] private Vector2 deadzoneSize = new Vector2(1.5f, 1f);

    [Header("Lookahead")]
    [SerializeField] private bool lookaheadEnabled = true;
    [Tooltip("How far (world units) the view is shifted ahead of the player.")]
    [SerializeField, Min(0f)] private float lookaheadDistance = 2f;
    [Tooltip("Seconds the lookahead offset takes to ease to its new value.")]
    [SerializeField, Min(0f)] private float lookaheadSmoothTime = 0.3f;
    [Tooltip("The player must move faster than this (units/s) for the lookahead direction to change.")]
    [SerializeField, Min(0f)] private float lookaheadMinSpeed = 2f;
    [Tooltip("The new direction must be held this long before the lookahead flips, so a tiny tap does not swing the view.")]
    [SerializeField, Min(0f)] private float lookaheadDirectionDelay = 0.1f;

    [Header("Room bounds (Confine Camera Bounds)")]
    [SerializeField] private bool confineToBounds = false;
    [Tooltip("Optional. If assigned, its world bounds are the room (works with a trigger BoxCollider2D drawn over the room). " +
             "Otherwise the Min / Max below are used.")]
    [SerializeField] private Collider2D roomBoundsCollider;
    [SerializeField] private Vector2 minBounds = new Vector2(-20f, -10f);
    [SerializeField] private Vector2 maxBounds = new Vector2(20f, 10f);

    // ------------------------------------------------------------------ Runtime
    private Camera cam;
    private CameraEffects effects;
    private Rigidbody2D targetBody;
    private PlayerRespawn boundRespawn;

    private bool initialized;
    private float z;

    private Vector2 focus;          // deadzone-resolved point the view is built around (without lookahead)
    private Vector2 logicalPos;     // smoothed camera position without shake
    private float velX, velY;

    private float lookaheadOffset, lookaheadVel;
    private int lookaheadDir, pendingDir;
    private float pendingTimer;
    private float prevTargetX;

    // ------------------------------------------------------------------ Public API
    public Transform Target => target;

    public void SetTarget(Transform newTarget, bool snap = true)
    {
        Unbind();
        target = newTarget;
        initialized = false;             // re-initialise (and snap) on the next LateUpdate
        if (!snap) initialized = TryInitialize(false);
    }

    /// <summary>Confines the view to a room rectangle. The camera glides to the new framing using the normal damping.</summary>
    public void SetRoomBounds(Vector2 min, Vector2 max)
    {
        roomBoundsCollider = null;
        minBounds = min;
        maxBounds = max;
        confineToBounds = true;
    }

    public void ClearRoomBounds() => confineToBounds = false;

    /// <summary>Instantly places the camera on the target (clears velocities and the deadzone offset).</summary>
    public void SnapToTarget()
    {
        if (target == null) return;

        float dir = lookaheadEnabled ? lookaheadDir : 0f;
        lookaheadOffset = dir * lookaheadDistance;
        lookaheadVel = 0f;
        velX = velY = 0f;
        pendingDir = 0;
        pendingTimer = 0f;
        prevTargetX = target.position.x;

        Vector2 desired = ClampToRoom((Vector2)target.position + new Vector2(lookaheadOffset, 0f));
        focus = desired - new Vector2(lookaheadOffset, 0f);
        logicalPos = desired;
        ApplyPosition();
    }

    // ------------------------------------------------------------------ Lifecycle
    private void Awake()
    {
        cam = GetComponent<Camera>();
        effects = GetComponent<CameraEffects>();
        z = transform.position.z;

        if (!cam.orthographic)
            Debug.LogWarning("CameraController2D expects an orthographic camera (view size is used for the room bounds).", this);
    }

    private void OnDisable() => Unbind();

    private void LateUpdate()
    {
        if (!initialized)
        {
            initialized = TryInitialize(true);
            if (!initialized) return;
        }

        float dt = Time.deltaTime;

        UpdateLookahead(dt);
        UpdateDeadzone();

        // Desired centre, confined to the room. Re-derive the focus from the clamped result so that pushing against a
        // room edge does not "wind up" an invisible offset that would delay the camera when the player turns around.
        Vector2 look = new Vector2(lookaheadOffset, 0f);
        Vector2 desired = ClampToRoom(focus + look);
        focus = desired - look;

        logicalPos.x = Mathf.SmoothDamp(logicalPos.x, desired.x, ref velX, smoothTimeX, Mathf.Infinity, dt);
        logicalPos.y = Mathf.SmoothDamp(logicalPos.y, desired.y, ref velY, smoothTimeY, Mathf.Infinity, dt);

        ApplyPosition();
    }

    // ------------------------------------------------------------------ Follow logic
    private bool TryInitialize(bool snap)
    {
        if (target == null)
        {
            PlayerController2D found = FindFirstObjectByType<PlayerController2D>();
            if (found == null) return false;
            target = found.transform;
        }

        targetBody = target.GetComponent<Rigidbody2D>();
        if (target.TryGetComponent(out PlayerController2D pc)) lookaheadDir = pc.Facing >= 0 ? 1 : -1;

        Bind();
        if (snap) SnapToTarget();
        return true;
    }

    private void UpdateLookahead(float dt)
    {
        float vx = HorizontalSpeed(dt);
        int moveDir = Mathf.Abs(vx) >= lookaheadMinSpeed ? (vx > 0f ? 1 : -1) : 0;

        // Direction only flips after being held for lookaheadDirectionDelay. When the player stops, the last
        // direction is kept, so the view does not drift back to centre every time they pause.
        if (moveDir == 0 || moveDir == lookaheadDir)
        {
            pendingDir = 0;
            pendingTimer = 0f;
        }
        else
        {
            if (pendingDir != moveDir) { pendingDir = moveDir; pendingTimer = 0f; }
            pendingTimer += dt;
            if (pendingTimer >= lookaheadDirectionDelay)
            {
                lookaheadDir = moveDir;
                pendingDir = 0;
            }
        }

        float goal = lookaheadEnabled ? lookaheadDir * lookaheadDistance : 0f;
        lookaheadOffset = Mathf.SmoothDamp(lookaheadOffset, goal, ref lookaheadVel, lookaheadSmoothTime, Mathf.Infinity, dt);
    }

    private float HorizontalSpeed(float dt)
    {
        float x = target.position.x;
        float speed = targetBody != null
            ? targetBody.linearVelocity.x                       // physics velocity: stable even without interpolation
            : (dt > 0f ? (x - prevTargetX) / dt : 0f);
        prevTargetX = x;
        return speed;
    }

    /// <summary>The focus only moves by the amount the target sticks out of the box; inside the box nothing happens.</summary>
    private void UpdateDeadzone()
    {
        Vector2 half = deadzoneSize * 0.5f;
        Vector2 delta = (Vector2)target.position - focus;

        if (delta.x > half.x) focus.x += delta.x - half.x;
        else if (delta.x < -half.x) focus.x += delta.x + half.x;

        if (delta.y > half.y) focus.y += delta.y - half.y;
        else if (delta.y < -half.y) focus.y += delta.y + half.y;
    }

    // ------------------------------------------------------------------ Room bounds
    private bool TryGetRoom(out Rect room)
    {
        room = default;
        if (!confineToBounds) return false;

        if (roomBoundsCollider != null)
        {
            Bounds b = roomBoundsCollider.bounds;
            room = Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
            return true;
        }

        room = Rect.MinMaxRect(minBounds.x, minBounds.y, maxBounds.x, maxBounds.y);
        return true;
    }

    /// <summary>Keeps the whole view inside the room; an axis where the room is smaller than the view is centred (static room).</summary>
    private Vector2 ClampToRoom(Vector2 p)
    {
        if (!TryGetRoom(out Rect room)) return p;

        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;

        p.x = room.width <= halfW * 2f ? room.center.x : Mathf.Clamp(p.x, room.xMin + halfW, room.xMax - halfW);
        p.y = room.height <= halfH * 2f ? room.center.y : Mathf.Clamp(p.y, room.yMin + halfH, room.yMax - halfH);
        return p;
    }

    // ------------------------------------------------------------------ Output
    private void ApplyPosition()
    {
        Vector2 shake = effects != null ? effects.CurrentOffset : Vector2.zero;
        transform.position = new Vector3(logicalPos.x + shake.x, logicalPos.y + shake.y, z);
    }

    // ------------------------------------------------------------------ Respawn hookup
    private void Bind()
    {
        if (!snapOnRespawn || target == null) return;
        boundRespawn = target.GetComponent<PlayerRespawn>();
        if (boundRespawn != null) boundRespawn.Respawned += OnRespawned;
    }

    private void Unbind()
    {
        if (boundRespawn != null) boundRespawn.Respawned -= OnRespawned;
        boundRespawn = null;
    }

    private void OnRespawned() => SnapToTarget();

    // ------------------------------------------------------------------ Gizmos
    private void OnDrawGizmosSelected()
    {
        Camera c = cam != null ? cam : GetComponent<Camera>();

        // Deadzone box (yellow) around the focus point.
        Vector2 center = Application.isPlaying && initialized ? focus : (Vector2)transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(center, deadzoneSize);

        // Room rectangle (cyan).
        if (!confineToBounds) return;
        Rect room = roomBoundsCollider != null
            ? Rect.MinMaxRect(roomBoundsCollider.bounds.min.x, roomBoundsCollider.bounds.min.y,
                              roomBoundsCollider.bounds.max.x, roomBoundsCollider.bounds.max.y)
            : Rect.MinMaxRect(minBounds.x, minBounds.y, maxBounds.x, maxBounds.y);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(room.center, room.size);
    }
}
