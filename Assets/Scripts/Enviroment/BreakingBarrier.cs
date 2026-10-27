using System.Collections;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A solid barrier the player can smash by dashing into it (Earth prototype design).
/// Visual states:
///   - Intact (Normal): shows <see cref="normalSprite"/> (IMG_0491), Collider2D enabled.
///   - Broken (Smashed): shows <see cref="brokenSprite"/> (IMG_0492), Collider2D disabled immediately
///     so the dashing player passes straight through.
///
/// Gameplay effects on dash impact:
///   1. Disables Collider2D and switches the sprite to <see cref="brokenSprite"/> (IMG_0492).
///   2. Reduces Stress immediately by -30f (via PlayerStress.ApplyBreakingBarrierStress).
///   3. Refunds 1 Air Dash immediately (PlayerController2D.RefundAirDash) so a new dash can be
///      chained in mid-air right after the impact.
///   4. Optionally resets back to Intact when the player respawns (<see cref="resetOnPlayerRespawn"/>).
///
/// PlayerController2D breaks the barrier proactively - it sweeps along the dash path before the
/// physics step (BreakBarriersAlongStep) - so the dash never loses momentum to the collision.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class BreakingBarrier : MonoBehaviour, IBreakable
{
    public enum BarrierTheme { GrassDirt_Green, CaveDirt_Brown, SnowIce_Blue, Custom }

    [Header("Visuals - 2-Stage Sprites (auto-loaded from Assets/Tileset)")]
    [Tooltip("Visual theme preset (GrassDirt_Green = IMG_0491/0492, CaveDirt_Brown = IMG_0548/0549, SnowIce_Blue = IMG_0555/0556).")]
    [SerializeField] private BarrierTheme barrierTheme = BarrierTheme.GrassDirt_Green;
    [Tooltip("Stage 1 (Intact): solid barrier wall (IMG_0491).")]
    [SerializeField] private Sprite normalSprite;
    [Tooltip("Stage 2 (Broken): smashed barrier wall (IMG_0492).")]
    [SerializeField] private Sprite brokenSprite;
    [SerializeField, HideInInspector] private BarrierTheme lastValidatedTheme = (BarrierTheme)(-1);

    [Header("References (auto-filled if empty)")]
    [SerializeField] private Collider2D[] colliders;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Break & Respawn Behaviour")]
    [Tooltip("If true (default), the smashed sprite (IMG_0492) shows briefly on impact and then the barrier disappears.")]
    [SerializeField] private bool hideAfterBreak = true;
    [Tooltip("How long IMG_0492 is shown before the barrier disappears.")]
    [SerializeField, Min(0f)] private float brokenDisplayDuration = 0.25f;
    [Tooltip("Destroy the GameObject after breaking (only used if hideAfterBreak is true and resetOnPlayerRespawn is false).")]
    [SerializeField] private bool destroyOnBreak = false;
    [Tooltip("Automatically restore the barrier when the player dies and respawns.")]
    [SerializeField] private bool resetOnPlayerRespawn = true;

    [Header("Events")]
    [Tooltip("Hook for particles / audio when the barrier breaks.")]
    public UnityEvent OnBroken;

    private bool isBroken;
    private Color baseColor = Color.white;
    private PlayerRespawn trackedRespawn;
    private Coroutine breakRoutine;

    // ------------------------------------------------------------------ Lifecycle & Editor Auto-Wiring
    private void Reset()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        DetectThemeFromCurrentSprite();
        lastValidatedTheme = barrierTheme;
        AutoLoadSpritesInEditor(true);
        colliders = GetComponents<Collider2D>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        if (normalSprite != null && spriteRenderer.sprite == null)
            spriteRenderer.sprite = normalSprite;

        var box = GetComponent<BoxCollider2D>();
        if (box != null && spriteRenderer != null && spriteRenderer.sprite != null)
            box.size = spriteRenderer.sprite.bounds.size;
    }

    private void OnValidate()
    {
        if (colliders == null || colliders.Length == 0) colliders = GetComponents<Collider2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        bool themeChanged = barrierTheme != lastValidatedTheme && lastValidatedTheme != (BarrierTheme)(-1);
        lastValidatedTheme = barrierTheme;

        AutoLoadSpritesInEditor(themeChanged);
        if (!Application.isPlaying && spriteRenderer != null && (spriteRenderer.sprite == null || themeChanged) && normalSprite != null)
            spriteRenderer.sprite = normalSprite;
    }

    private void DetectThemeFromCurrentSprite()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null) return;
        string n = spriteRenderer.sprite.name;
        if (n.StartsWith("IMG_0548") || n.StartsWith("IMG_0549")) barrierTheme = BarrierTheme.CaveDirt_Brown;
        else if (n.StartsWith("IMG_0555") || n.StartsWith("IMG_0556")) barrierTheme = BarrierTheme.SnowIce_Blue;
        else if (n.StartsWith("IMG_049")) barrierTheme = BarrierTheme.GrassDirt_Green;
    }

    private void AutoLoadSpritesInEditor(bool forceOverwrite = false)
    {
#if UNITY_EDITOR
        if (barrierTheme == BarrierTheme.Custom) return;

        string pNormal, pBroken;
        switch (barrierTheme)
        {
            case BarrierTheme.CaveDirt_Brown:
                pNormal = "Assets/Tileset/IMG_0548.png";
                pBroken = "Assets/Tileset/IMG_0549.png";
                break;

            case BarrierTheme.SnowIce_Blue:
                pNormal = "Assets/Tileset/IMG_0555.png";
                pBroken = "Assets/Tileset/IMG_0556.png";
                break;

            default: // GrassDirt_Green
                pNormal = "Assets/Tileset/IMG_0491.png";
                pBroken = "Assets/Tileset/IMG_0492.png";
                break;
        }

        if (forceOverwrite || normalSprite == null)
            normalSprite = AssetDatabase.LoadAssetAtPath<Sprite>(pNormal);
        if (forceOverwrite || brokenSprite == null)
            brokenSprite = AssetDatabase.LoadAssetAtPath<Sprite>(pBroken);
#endif
    }

    private void Awake()
    {
        AutoLoadSpritesInEditor();
        if (colliders == null || colliders.Length == 0) colliders = GetComponents<Collider2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
            // Only take over the visuals when nothing is assigned yet (e.g. attached via the Inspector),
            // so greybox blocks that already draw their own square sprite keep their look.
            if (normalSprite != null && spriteRenderer.sprite == null)
                spriteRenderer.sprite = normalSprite;
        }
    }

    private void OnEnable()
    {
        if (resetOnPlayerRespawn)
        {
            trackedRespawn = FindAnyObjectByType<PlayerRespawn>();
            if (trackedRespawn != null)
                trackedRespawn.Respawned += ResetBarrier;
        }
    }

    private void OnDisable()
    {
        if (trackedRespawn != null)
        {
            trackedRespawn.Respawned -= ResetBarrier;
            trackedRespawn = null;
        }
    }

    // ------------------------------------------------------------------ Public API
    /// <summary>IBreakable entry point (without a player reference the -30f stress and dash refund are skipped).</summary>
    public void Break() => Break(null);

    /// <summary>Breaks the barrier, shows IMG_0492, disables colliders, and applies -30f stress + 1 Air Dash refund.</summary>
    public void Break(PlayerController2D player)
    {
        if (isBroken) return;
        isBroken = true;

        // 1. Disable colliders immediately so the dashing player passes straight through.
        SetCollidersEnabled(false);
        SoundManager.PlayBarrierBreak();

        // 2. Show the broken sprite (IMG_0492).
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
            spriteRenderer.color = baseColor;
            // Swap to the smashed sprite only when this component owns the visuals (intact sprite or none).
            if (brokenSprite != null && (spriteRenderer.sprite == null || spriteRenderer.sprite == normalSprite))
                spriteRenderer.sprite = brokenSprite;
        }

        // 3. Refund 1 Air Dash & apply -30f Stress relief.
        if (player != null)
        {
            player.RefundAirDash();
            if (player.TryGetComponent(out PlayerStress stress))
            {
                stress.ApplyBreakingBarrierStress(); // -30f instant
            }
            if (trackedRespawn == null && resetOnPlayerRespawn && player.TryGetComponent(out PlayerRespawn pr))
            {
                trackedRespawn = pr;
                trackedRespawn.Respawned += ResetBarrier;
            }
        }

        if (CameraEffects.Instance != null)
            CameraEffects.Instance.MicroShake();

        OnBroken?.Invoke();

        if (hideAfterBreak && gameObject.activeInHierarchy)
        {
            if (breakRoutine != null) StopCoroutine(breakRoutine);
            breakRoutine = StartCoroutine(HideAfterDelayRoutine());
        }
    }

    /// <summary>Restores the barrier to its solid intact state (IMG_0491).</summary>
    public void ResetBarrier()
    {
        if (breakRoutine != null)
        {
            StopCoroutine(breakRoutine);
            breakRoutine = null;
        }

        isBroken = false;
        SetCollidersEnabled(true);

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
            spriteRenderer.color = baseColor;
            if (normalSprite != null && (spriteRenderer.sprite == null || spriteRenderer.sprite == brokenSprite))
                spriteRenderer.sprite = normalSprite;
        }
    }

    private IEnumerator HideAfterDelayRoutine()
    {
        yield return new WaitForSeconds(brokenDisplayDuration);
        if (destroyOnBreak && !resetOnPlayerRespawn)
        {
            Destroy(gameObject);
        }
        else if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }
        breakRoutine = null;
    }

    private void SetCollidersEnabled(bool value)
    {
        if (colliders == null) return;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = value;
        }
    }
}