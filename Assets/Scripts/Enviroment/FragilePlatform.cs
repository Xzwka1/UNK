using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Fragile / crumbling ground. Attach to a ground object that has one or more (non-trigger) Collider2D.
///
/// State machine:
///   Normal    -> solid ground.
///   Cracking  -> started when the player stands on top of it (or grips it). Lasts <see cref="crackDuration"/> (0.5 s),
///                the visual shakes. The timer is COMMITTED: jumping off does not break it early, does not reset it,
///                and touching it again while cracking does not restart it (see <see cref="cancelCrackWhenPlayerLeaves"/>
///                to opt in to the "cancel if the player leaves" design instead).
///   Destroyed -> colliders disabled, visual hidden. Lasts <see cref="destroyedDuration"/> (3.0 s).
///   Reforming -> colliders re-enabled, visual fades back in for <see cref="reformDuration"/>, then back to Normal.
///
/// Coyote time: PlayerController2D senses the ground with a physics query every FixedUpdate. The moment this platform's
/// colliders are disabled the player is no longer grounded, and because the coyote timer only starts counting down when
/// grounded becomes false, the player automatically keeps a full coyote-time jump window after the floor vanishes.
/// Nothing needs to be forced from this script.
///
/// NOTE ON TIMING: timers use Time.deltaTime (scaled, so they pause during a Freeze Frame) and are checked once per
/// frame, so a transition happens on the first frame at or after the configured time (at most one frame late).
/// </summary>
[DisallowMultipleComponent]
public class FragilePlatform : MonoBehaviour
{
    public enum PlatformState { Normal, Cracking, Destroyed, Reforming }

    // ------------------------------------------------------------------ Inspector: timing
    [Header("Timing (seconds)")]
    [Tooltip("How long the platform shakes/cracks before it breaks.")]
    [SerializeField, Min(0f)] private float crackDuration = 0.5f;
    [Tooltip("How long the platform stays gone before it starts to reform.")]
    [SerializeField, Min(0f)] private float destroyedDuration = 3f;
    [Tooltip("Length of the Reforming visual (fade-in / animation). The collider is already solid during this time.")]
    [SerializeField, Min(0f)] private float reformDuration = 0.25f;

    // ------------------------------------------------------------------ Inspector: trigger rules
    [Header("Trigger rules")]
    [Tooltip("Start cracking when the player stands on top of the platform.")]
    [SerializeField] private bool triggerOnStand = true;
    [Tooltip("Start cracking when the player grips (Ctrl) the platform.")]
    [SerializeField] private bool triggerOnGrip = true;
    [Tooltip("The player's feet must be at most this far below the platform's top edge to count as 'standing on top'.")]
    [SerializeField, Min(0f)] private float standTolerance = 0.15f;
    [Tooltip("OFF (default, Celeste-style): once cracking has started it always breaks after the crack duration, even if the player jumps off.\n" +
             "ON: if the player leaves before it breaks, the crack is cancelled and the platform returns to Normal.")]
    [SerializeField] private bool cancelCrackWhenPlayerLeaves = false;

    // ------------------------------------------------------------------ Inspector: reform
    [Header("Reform")]
    [Tooltip("If the player is inside the platform's area when it is time to reform, wait until they are out, so the " +
             "collider never pops on top of them. (The 3 s is a minimum; the wait only happens when the area is occupied.)")]
    [SerializeField] private bool waitForClearBeforeReform = true;
    [SerializeField] private LayerMask reformBlockerMask = ~0;

    // ------------------------------------------------------------------ Inspector: feedback
    [Header("Heavy landing feedback")]
    [Tooltip("Landing on top of the platform faster than this (units/s) triggers a Micro Shake on the camera (needs a CameraEffects in the scene).")]
    [SerializeField, Min(0f)] private float heavyLandingSpeed = 14f;

    // ------------------------------------------------------------------ Inspector: references
    [Header("References (auto-filled if empty)")]
    [Tooltip("Colliders that are switched off while Destroyed. Defaults to every Collider2D on this object.")]
    [SerializeField] private Collider2D[] colliders;
    [Tooltip("CHILD transform that is shaken while cracking. Do NOT use the object that holds the collider. " +
             "If empty and the sprite is on a child, that child is used; otherwise no positional shake is applied.")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("Optional. Triggers below are set on state changes.")]
    [SerializeField] private Animator animator;

    // ------------------------------------------------------------------ Inspector: visuals
    [Header("Visuals - sprites (optional; empty = keep the current sprite)")]
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite crackingSprite;
    [SerializeField] private Sprite reformingSprite;

    [Header("Visuals - placeholders / tuning")]
    [Tooltip("Multiplies the sprite colour while cracking (handy placeholder when there is no crack art).")]
    [SerializeField] private Color crackingTint = new Color(1f, 0.75f, 0.6f, 1f);
    [Tooltip("Max shake offset of the visual while cracking (world units).")]
    [SerializeField, Min(0f)] private float shakeAmplitude = 0.05f;
    [Tooltip("Shake position changes per second.")]
    [SerializeField, Min(1f)] private float shakeFrequency = 40f;
    [Tooltip("Hide the sprite while Destroyed. Turn off if you animate the destruction with the Animator.")]
    [SerializeField] private bool hideSpriteWhenDestroyed = true;
    [Tooltip("Fade the sprite alpha from 0 to 1 during Reforming.")]
    [SerializeField] private bool fadeInOnReform = true;

    [Header("Animator trigger names (empty = not used)")]
    [SerializeField] private string normalTrigger = "Normal";
    [SerializeField] private string crackingTrigger = "Crack";
    [SerializeField] private string destroyedTrigger = "Break";
    [SerializeField] private string reformingTrigger = "Reform";

    // ------------------------------------------------------------------ Inspector: events
    [Header("Events (Visual State hooks)")]
    public UnityEvent onNormal;
    public UnityEvent onCracking;
    public UnityEvent onDestroyed;
    public UnityEvent onReforming;

    /// <summary>C# alternative to the UnityEvents above. Raised after every state change.</summary>
    public event System.Action<PlatformState> StateChanged;

    // ------------------------------------------------------------------ Runtime
    public PlatformState State { get; private set; } = PlatformState.Normal;

    private float stateTimer;
    private float shakeTimer;
    private Bounds lastBounds;                 // world bounds while the colliders were enabled
    private Vector3 visualBasePos;
    private Color baseColor = Color.white;
    private Sprite originalSprite;

    private readonly List<PlayerController2D> contacts = new List<PlayerController2D>();
    private readonly Collider2D[] overlapBuffer = new Collider2D[8];

    // ------------------------------------------------------------------ Lifecycle
    private void Reset()
    {
        colliders = GetComponents<Collider2D>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void Awake()
    {
        if (colliders == null || colliders.Length == 0) colliders = GetComponents<Collider2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (visualRoot == null && spriteRenderer != null && spriteRenderer.transform != transform)
            visualRoot = spriteRenderer.transform;
        if (visualRoot != null) visualBasePos = visualRoot.localPosition;

        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
            originalSprite = spriteRenderer.sprite;
        }

        lastBounds = ComputeBounds();
        ApplyStateVisuals(PlatformState.Normal);
    }

    private void OnDisable() => contacts.Clear();

    private void FixedUpdate()
    {
        if (State != PlatformState.Normal && State != PlatformState.Cracking) return;

        lastBounds = ComputeBounds();
        bool interacting = IsPlayerInteracting();

        if (State == PlatformState.Normal && interacting)
            SetState(PlatformState.Cracking);
        else if (State == PlatformState.Cracking && cancelCrackWhenPlayerLeaves && !interacting)
            SetState(PlatformState.Normal);
    }

    private void Update()
    {
        stateTimer += Time.deltaTime;

        switch (State)
        {
            case PlatformState.Cracking:
                TickShake();
                if (stateTimer >= crackDuration) Break();
                break;

            case PlatformState.Destroyed:
                if (stateTimer >= destroyedDuration && (!waitForClearBeforeReform || !IsAreaOccupied()))
                    BeginReform();
                break;

            case PlatformState.Reforming:
                if (fadeInOnReform && spriteRenderer != null)
                {
                    float t = reformDuration > 0f ? Mathf.Clamp01(stateTimer / reformDuration) : 1f;
                    SetAlpha(t);
                }
                if (stateTimer >= reformDuration) SetState(PlatformState.Normal);
                break;
        }
    }

    // ------------------------------------------------------------------ Public API
    /// <summary>Breaks the platform immediately (e.g. from an explosion). Ignored unless Normal or Cracking.</summary>
    public void ForceBreak()
    {
        if (State == PlatformState.Normal || State == PlatformState.Cracking) Break();
    }

    /// <summary>Restores the platform to a solid Normal state right away (e.g. when the player respawns).</summary>
    public void ResetPlatform()
    {
        SetCollidersEnabled(true);
        contacts.Clear();
        SetState(PlatformState.Normal);
    }

    // ------------------------------------------------------------------ State transitions
    private void SetState(PlatformState next)
    {
        State = next;
        stateTimer = 0f;
        shakeTimer = 0f;

        ApplyStateVisuals(next);
        FireAnimatorTrigger(next);

        switch (next)
        {
            case PlatformState.Normal: onNormal?.Invoke(); break;
            case PlatformState.Cracking: onCracking?.Invoke(); break;
            case PlatformState.Destroyed: onDestroyed?.Invoke(); break;
            case PlatformState.Reforming: onReforming?.Invoke(); break;
        }

        StateChanged?.Invoke(next);
    }

    private void Break()
    {
        lastBounds = ComputeBounds();        // remember the footprint before the colliders go away
        SetCollidersEnabled(false);          // player falls; coyote time is handled by PlayerController2D
        contacts.Clear();
        SetState(PlatformState.Destroyed);
    }

    private void BeginReform()
    {
        SetCollidersEnabled(true);           // solid again from the first frame of Reforming
        SetState(PlatformState.Reforming);
    }

    // ------------------------------------------------------------------ Player detection
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.gameObject.TryGetComponent(out PlayerController2D player)) return;
        if (!contacts.Contains(player)) contacts.Add(player);

        // Heavy landing on top -> Micro Shake.
        bool solid = State == PlatformState.Normal || State == PlatformState.Cracking;
        if (solid
            && Mathf.Abs(collision.relativeVelocity.y) >= heavyLandingSpeed
            && IsStandingOnTop(player)
            && CameraEffects.Instance != null)
        {
            CameraEffects.Instance.MicroShake();
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.TryGetComponent(out PlayerController2D player))
            contacts.Remove(player);
    }

    /// <summary>
    /// Polled every FixedUpdate instead of relying on OnCollisionStay2D, because a resting Rigidbody2D goes to sleep and
    /// stops sending Stay callbacks (and a player can press Grip after already touching the platform).
    /// </summary>
    private bool IsPlayerInteracting()
    {
        for (int i = contacts.Count - 1; i >= 0; i--)
        {
            PlayerController2D p = contacts[i];
            if (p == null) { contacts.RemoveAt(i); continue; }

            if (triggerOnGrip && p.IsGripping) return true;
            if (triggerOnStand && p.IsGrounded && IsStandingOnTop(p)) return true;
        }
        return false;
    }

    private bool IsStandingOnTop(PlayerController2D player)
    {
        if (!player.TryGetComponent(out Collider2D playerCollider)) return false;
        return playerCollider.bounds.min.y >= lastBounds.max.y - standTolerance;
    }

    private bool IsAreaOccupied()
    {
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(reformBlockerMask);

        int n = Physics2D.OverlapBox(lastBounds.center, lastBounds.size, 0f, filter, overlapBuffer);
        for (int i = 0; i < n; i++)
            if (overlapBuffer[i].TryGetComponent(out PlayerController2D _)) return true;
        return false;
    }

    // ------------------------------------------------------------------ Colliders / bounds
    private void SetCollidersEnabled(bool value)
    {
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = value;
    }

    private Bounds ComputeBounds()
    {
        bool has = false;
        Bounds b = lastBounds;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D c = colliders[i];
            if (c == null || !c.enabled) continue;
            if (!has) { b = c.bounds; has = true; }
            else b.Encapsulate(c.bounds);
        }
        return b;
    }

    // ------------------------------------------------------------------ Visuals
    private void ApplyStateVisuals(PlatformState s)
    {
        ResetShake();

        switch (s)
        {
            case PlatformState.Normal:
                SetSprite(normalSprite);
                SetVisible(true);
                SetColor(baseColor);
                break;

            case PlatformState.Cracking:
                SetSprite(crackingSprite);
                SetVisible(true);
                SetColor(baseColor * crackingTint);
                break;

            case PlatformState.Destroyed:
                SetVisible(!hideSpriteWhenDestroyed);
                break;

            case PlatformState.Reforming:
                SetSprite(reformingSprite);
                SetVisible(true);
                SetColor(baseColor);
                if (fadeInOnReform) SetAlpha(0f);
                break;
        }
    }

    private void TickShake()
    {
        if (visualRoot == null) return;

        shakeTimer -= Time.deltaTime;
        if (shakeTimer > 0f) return;

        shakeTimer = 1f / shakeFrequency;
        Vector2 r = Random.insideUnitCircle * shakeAmplitude;
        visualRoot.localPosition = visualBasePos + new Vector3(r.x, r.y * 0.5f, 0f);
    }

    private void ResetShake()
    {
        if (visualRoot != null) visualRoot.localPosition = visualBasePos;
    }

    /// <summary>Preferred sprite, falling back to the Normal sprite, then to whatever the renderer had at start.</summary>
    private void SetSprite(Sprite preferred)
    {
        if (spriteRenderer == null) return;
        Sprite s = preferred != null ? preferred : (normalSprite != null ? normalSprite : originalSprite);
        if (s != null) spriteRenderer.sprite = s;
    }

    private void SetVisible(bool visible)
    {
        if (spriteRenderer != null) spriteRenderer.enabled = visible;
    }

    private void SetColor(Color c)
    {
        if (spriteRenderer != null) spriteRenderer.color = c;
    }

    private void SetAlpha(float a)
    {
        Color c = baseColor;
        c.a = baseColor.a * a;
        SetColor(c);
    }

    private void FireAnimatorTrigger(PlatformState s)
    {
        if (animator == null) return;

        string trigger = s switch
        {
            PlatformState.Normal => normalTrigger,
            PlatformState.Cracking => crackingTrigger,
            PlatformState.Destroyed => destroyedTrigger,
            _ => reformingTrigger
        };

        if (!string.IsNullOrEmpty(trigger)) animator.SetTrigger(trigger);
    }
}
