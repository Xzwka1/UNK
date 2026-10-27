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
///   5. Shake       - the offset from CameraEffects (if present) is added on top and clamped to the room bounds
///                    so screen shake near map edges never reveals outside the background art.
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
    [SerializeField] private bool confineToBounds = true;
    [Tooltip("Optional. If assigned and autoBoundsFromScene is false, its world bounds are the room.")]
    [SerializeField] private Collider2D roomBoundsCollider;
    [SerializeField] private Vector2 minBounds = new Vector2(-20f, -10f);
    [SerializeField] private Vector2 maxBounds = new Vector2(20f, 10f);
    [Tooltip("Automatically locks camera bounds to the Background SpriteRenderer (ignoring stray tiles or portals outside the BG edge) so the camera never shows past the map background.")]
    [SerializeField] private bool autoBoundsFromScene = true;
    [Tooltip("Small inward margin (world units) from the background edge so sub-pixel seams or shakes never show outside the BG.")]
    [SerializeField, Min(0f)] private float boundsEdgeInset = 0.08f;

    [Header("Aspect Ratio (16:9 / 1920x1080)")]
    [Tooltip("Letterbox / pillarbox the view to a fixed aspect ratio so every window size shows the same area.")]
    [SerializeField] private bool lockAspectRatio = true;
    [SerializeField] private Vector2 aspectRatio = new Vector2(16f, 9f);

    private Camera letterboxCam;
    private int lastScreenW, lastScreenH;

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
        if (GetComponent<AudioListener>() == null && FindFirstObjectByType<AudioListener>() == null)
        {
            gameObject.AddComponent<AudioListener>();
        }
        if (lockAspectRatio) ApplyAspect(true);

        if (!cam.orthographic)
            Debug.LogWarning("CameraController2D expects an orthographic camera (view size is used for the room bounds).", this);
    }

    private void OnDisable() => Unbind();

    private void LateUpdate()
    {
        if (lockAspectRatio) ApplyAspect(false);

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
        logicalPos = ClampToRoom(logicalPos);

        ApplyPosition();
    }

    // ------------------------------------------------------------------ Follow logic
    private bool TryInitialize(bool snap)
    {
        if (target == null)
        {
            PlayerController2D found = FindAnyObjectByType<PlayerController2D>();
            if (found == null) return false;
            target = found.transform;
        }

        targetBody = target.GetComponent<Rigidbody2D>();
        if (target.TryGetComponent(out PlayerController2D pc)) lookaheadDir = pc.Facing >= 0 ? 1 : -1;

        Bind();
        if (autoBoundsFromScene) BuildAutoBounds();
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

    // ------------------------------------------------------------------ Aspect ratio
    private void ApplyAspect(bool force)
    {
        if (!force && Screen.width == lastScreenW && Screen.height == lastScreenH) return;
        lastScreenW = Screen.width;
        lastScreenH = Screen.height;

        float want = aspectRatio.x / aspectRatio.y;
        float have = (float)Screen.width / Screen.height;
        Rect r = new Rect(0f, 0f, 1f, 1f);
        if (have > want)        // window too wide -> pillarbox
        {
            r.width = want / have;
            r.x = (1f - r.width) * 0.5f;
        }
        else if (have < want)   // window too tall -> letterbox
        {
            r.height = have / want;
            r.y = (1f - r.height) * 0.5f;
        }
        cam.rect = r;

        // A tiny black camera behind the main one clears the bars so no stale pixels show.
        if (letterboxCam == null)
        {
            var go = new GameObject("LetterboxBackground");
            go.transform.SetParent(transform, false);
            letterboxCam = go.AddComponent<Camera>();
            letterboxCam.clearFlags = CameraClearFlags.SolidColor;
            letterboxCam.backgroundColor = Color.black;
            letterboxCam.cullingMask = 0;
            letterboxCam.depth = cam.depth - 100f;
            var al = go.GetComponent<AudioListener>();
            if (al != null) Destroy(al);
        }
    }

    // ------------------------------------------------------------------ Room bounds
    /// <summary>
    /// Automatically locks the camera bounds to the map's Background SpriteRenderer
    /// (ignoring Tilemap edges, portals, or props that stick out past the background image).
    /// Falls back to roomBoundsCollider or all scene renderers if no large background sprite exists.
    /// </summary>
    [ContextMenu("Recalculate Bounds From Background")]
    public void BuildAutoBounds()
    {
        // 1. Prefer the full-map Background SpriteRenderer(s) (area >= 400 sq units, e.g. 77x43 or 43x76)
        //    so tiles or portals sticking out beyond the BG art never cause the camera to show empty space.
        bool hasBg = false;
        Bounds bgBounds = default;

        SpriteRenderer[] spriteRenderers = FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (SpriteRenderer sr in spriteRenderers)
        {
            if (sr.sprite == null || !sr.enabled) continue;
            if (target != null && sr.transform.IsChildOf(target)) continue;
            if (sr.transform.IsChildOf(transform)) continue;

            Bounds b = sr.bounds;
            float area = b.size.x * b.size.y;
            if (b.size.x >= 25f && b.size.y >= 15f && area >= 400f)
            {
                if (!hasBg)
                {
                    bgBounds = b;
                    hasBg = true;
                }
                else
                {
                    // If multiple full-map BG layers overlap (e.g. Map 4), intersect them so no layer's edge is crossed.
                    float interMinX = Mathf.Max(bgBounds.min.x, b.min.x);
                    float interMinY = Mathf.Max(bgBounds.min.y, b.min.y);
                    float interMaxX = Mathf.Min(bgBounds.max.x, b.max.x);
                    float interMaxY = Mathf.Min(bgBounds.max.y, b.max.y);

                    if (interMaxX - interMinX >= 25f && interMaxY - interMinY >= 15f)
                    {
                        bgBounds.SetMinMax(new Vector3(interMinX, interMinY, 0f), new Vector3(interMaxX, interMaxY, 0f));
                    }
                    else if (area > bgBounds.size.x * bgBounds.size.y)
                    {
                        bgBounds = b;
                    }
                }
            }
        }

        if (hasBg)
        {
            ApplyDetectedBounds(bgBounds);
            return;
        }

        // 2. If an explicit roomBoundsCollider is assigned and no large BG sprite exists, keep using it.
        if (roomBoundsCollider != null)
        {
            confineToBounds = true;
            return;
        }

        // 3. Fallback (e.g. Greybox scenes without a large BG sprite): encapsulate all visible map renderers.
        bool hasAny = false;
        Bounds total = default;
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!(r is SpriteRenderer) && !(r is UnityEngine.Tilemaps.TilemapRenderer)) continue;
            if (target != null && r.transform.IsChildOf(target)) continue;
            if (r.transform.IsChildOf(transform)) continue;
            Bounds b = r.bounds;
            if (b.size.x <= 0f || b.size.y <= 0f) continue;
            if (!hasAny) { total = b; hasAny = true; }
            else total.Encapsulate(b);
        }

        if (!hasAny) return;
        ApplyDetectedBounds(total);
    }

    private void ApplyDetectedBounds(Bounds b)
    {
        Vector2 min = (Vector2)b.min + new Vector2(boundsEdgeInset, boundsEdgeInset);
        Vector2 max = (Vector2)b.max - new Vector2(boundsEdgeInset, boundsEdgeInset);

        roomBoundsCollider = null;
        minBounds = min;
        maxBounds = max;
        confineToBounds = true;
    }

    private bool TryGetRoom(out Rect room)
    {
        room = default;
        if (!confineToBounds) return false;

        if (roomBoundsCollider != null)
        {
            Bounds b = roomBoundsCollider.bounds;
            room = Rect.MinMaxRect(
                b.min.x + boundsEdgeInset,
                b.min.y + boundsEdgeInset,
                b.max.x - boundsEdgeInset,
                b.max.y - boundsEdgeInset);
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
        float effectiveAspect = (lockAspectRatio && aspectRatio.y > 0f)
            ? (aspectRatio.x / aspectRatio.y)
            : cam.aspect;
        float halfW = halfH * effectiveAspect;

        p.x = room.width <= halfW * 2f ? room.center.x : Mathf.Clamp(p.x, room.xMin + halfW, room.xMax - halfW);
        p.y = room.height <= halfH * 2f ? room.center.y : Mathf.Clamp(p.y, room.yMin + halfH, room.yMax - halfH);
        return p;
    }

    // ------------------------------------------------------------------ Output
    private void ApplyPosition()
    {
        Vector2 shake = effects != null ? effects.CurrentOffset : Vector2.zero;
        // Clamp final position (including camera shake) so shake near map borders never peeks outside the BG art.
        Vector2 finalPos = ClampToRoom(logicalPos + shake);
        transform.position = new Vector3(finalPos.x, finalPos.y, z);
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
