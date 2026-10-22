using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// One-stop physics & visual setup for the player. Add this single component to a GameObject and it pulls in
/// everything else via RequireComponent (Rigidbody2D, CapsuleCollider2D, controller, stress, respawn), then:
///  - sizes the Hitbox (CapsuleCollider2D, 14x22 px) and gives it a zero-friction material,
///  - creates a child "Hurtbox" trigger (10x18 px) used for hazards,
///  - configures the child "Visual" with the character's SpriteRenderer, Animator (_Player.controller),
///    and PlayerAnimator2D (with bottom-pivoted sprite aligned flush with the bottom of the CapsuleCollider2D).
/// Runs in Reset (when added), OnValidate, Awake, and from the context menu "Apply Setup".
/// </summary>
[RequireComponent(typeof(PlayerController2D), typeof(PlayerRespawn))]
public class PlayerSetup : MonoBehaviour
{
    [Header("Pixel logic")]
    [SerializeField] private float pixelsPerUnit = 16f;
    [SerializeField] private Vector2 hitboxPixels = new Vector2(14f, 22f);
    [SerializeField] private Vector2 hurtboxPixels = new Vector2(10f, 18f);

    [Header("Character Sprite & Animation")]
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField] private Sprite defaultIdleSprite;
    [SerializeField] private Sprite crystalOverlaySprite;
    [SerializeField] private Sprite brittleShatterSprite;
    [Tooltip("Scale of the Visual child so the 500px character art fits the physics hitbox cleanly.")]
    [SerializeField] private Vector3 characterVisualScale = new Vector3(0.35f, 0.35f, 1f);
    [Tooltip("Local position offset of the Visual child so the bottom-pivoted character's feet sit flush with the bottom of the CapsuleCollider2D (-0.6875).")]
    [SerializeField] private Vector3 characterVisualOffset = new Vector3(0f, -0.6875f, 0f);
    [Tooltip("Target orthographic camera size for gameplay if the scene camera was zoomed far out (e.g. 20).")]
    [SerializeField] private float gameplayCameraSize = 6.5f;

    [Header("Greybox Fallback")]
    [SerializeField] private bool createVisual = true;
    [SerializeField] private Color visualColor = new Color(0.25f, 0.55f, 1f);

    private const string HurtboxName = "Hurtbox";
    private const string VisualName = "Visual";
    private const string CrystalOverlayName = "CrystalOverlay";

    private void Reset()
    {
        AutoLoadEditorAssets();
        Apply(false);
    }

    private void OnValidate()
    {
        AutoLoadEditorAssets();
        float ppu = Mathf.Max(1f, pixelsPerUnit);
        float halfHitboxHeight = (hitboxPixels.y / ppu) * 0.5f;
        if (characterVisualOffset.y > -0.2f)
            characterVisualOffset = new Vector3(0f, -halfHitboxHeight, 0f);
    }

    private void Awake()
    {
        AutoLoadEditorAssets();
        Apply(true);
    }

    [ContextMenu("Apply Setup")]
    public void ApplyFromMenu()
    {
        AutoLoadEditorAssets();
        Apply(Application.isPlaying);
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void AutoApplyInOpenScene()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying) return;
            var setups = FindObjectsByType<PlayerSetup>(FindObjectsSortMode.None);
            foreach (var s in setups)
            {
                if (s != null)
                {
                    s.ApplyFromMenu();
                    EditorUtility.SetDirty(s);
                    if (s.transform.Find(VisualName) != null)
                        EditorUtility.SetDirty(s.transform.Find(VisualName).gameObject);
                }
            }
        };
    }
#endif

    private void AutoLoadEditorAssets()
    {
#if UNITY_EDITOR
        if (animatorController == null)
            animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Player_Controller/_Player.controller");
        if (defaultIdleSprite == null)
            defaultIdleSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Spritesheet/spritesheet(Idle).png");
        if (crystalOverlaySprite == null)
            crystalOverlaySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Spritesheet/spritesheet(Overlay รอยร้าวผลึกแก้ว).png");
        if (brittleShatterSprite == null)
            brittleShatterSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Spritesheet/spritesheet(Slime_ร่างแข็ง).png");
#endif
    }

    private void Apply(bool runtime)
    {
        float ppu = Mathf.Max(1f, pixelsPerUnit);
        float halfHitboxHeight = (hitboxPixels.y / ppu) * 0.5f; // 0.6875f for 22px @ 16 PPU

        // --- Rigidbody: no rotation, smooth + safe at dash speed.
        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

        // --- Hitbox: capsule centered on Player (bottom is at y = -halfHitboxHeight).
        var cap = GetComponent<CapsuleCollider2D>();
        cap.direction = CapsuleDirection2D.Vertical;
        cap.size = hitboxPixels / ppu;
        cap.offset = Vector2.zero;
        cap.isTrigger = false;
        if (runtime)
            cap.sharedMaterial = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };

        // --- Hurtbox: separate trigger child, centred on the player.
        Transform hb = FindOrCreateChild(HurtboxName);
        var box = Ensure<BoxCollider2D>(hb.gameObject);
        box.isTrigger = true;
        box.size = hurtboxPixels / ppu;
        box.offset = Vector2.zero;
        Ensure<PlayerHurtbox>(hb.gameObject);

        // --- Overhead stress bar (built from sprites at runtime, so only added in Play mode).
        if (runtime) Ensure<StressBarHUD>(gameObject); // new hand-drawn vertical gauge (replaces StressBarOverhead)
        if (runtime) Ensure<LockoutFeedbackIcons>(gameObject);

        // --- Ensure gameplay camera isn't zoomed out to 20 units (which makes the character tiny/pixelated on screen).
        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.orthographic && mainCam.orthographicSize > 10f && gameplayCameraSize > 1f)
        {
            mainCam.orthographicSize = gameplayCameraSize;
        }

        // --- Visual: Character Sprite + Animator (or Greybox fallback if no character assets exist)
        if (createVisual)
        {
            Transform vis = FindOrCreateChild(VisualName);
            var sr = Ensure<SpriteRenderer>(vis.gameObject);
            sr.sortingOrder = 10;

            bool hasCharacterArt = animatorController != null || defaultIdleSprite != null;
            if (hasCharacterArt)
            {
                if (Mathf.Approximately(characterVisualScale.x, 0.28f))
                {
                    characterVisualScale = new Vector3(0.35f, 0.35f, 1f);
                }

                // All character sprites have bottom-center pivot (0.5, 0), so align Visual.y with the bottom of the CapsuleCollider2D (-halfHitboxHeight)
                if (characterVisualOffset.y > -0.2f)
                {
                    characterVisualOffset = new Vector3(0f, -halfHitboxHeight, 0f);
                }

                vis.localPosition = characterVisualOffset;
                vis.localScale = characterVisualScale;
                sr.color = Color.white;

                if (defaultIdleSprite != null && (sr.sprite == null || (runtime && sr.sprite == GreyboxSprites.Square)))
                    sr.sprite = defaultIdleSprite;

                var anim = Ensure<Animator>(vis.gameObject);
                if (animatorController != null)
                    anim.runtimeAnimatorController = animatorController;

                // Keep any legacy CrystalOverlay disabled so it doesn't render a duplicate body
                SpriteRenderer overlaySr = null;
                Transform overlayTf = vis.Find(CrystalOverlayName);
                if (overlayTf != null)
                {
                    overlaySr = overlayTf.GetComponent<SpriteRenderer>();
                    if (overlaySr != null) overlaySr.enabled = false;
                }

                var ctrl = GetComponent<PlayerController2D>();
                var stress = GetComponent<PlayerStress>();
                var respawn = GetComponent<PlayerRespawn>();

                if (stress != null)
                    stress.ConfigureVisuals(sr, overlaySr, brittleShatterSprite);

                var playerAnim = Ensure<PlayerAnimator2D>(gameObject);
                playerAnim.BindReferences(ctrl, stress, respawn, rb, anim, sr, overlaySr);
            }
            else
            {
                vis.localPosition = Vector3.zero;
                vis.localScale = new Vector3(hitboxPixels.x / ppu, hitboxPixels.y / ppu, 1f);
                sr.color = visualColor;
                if (runtime) sr.sprite = GreyboxSprites.Square;
            }
        }
    }

    private Transform FindOrCreateChild(string childName)
    {
        Transform t = transform.Find(childName);
        if (t == null)
        {
            var go = new GameObject(childName);
            t = go.transform;
            t.SetParent(transform, false);
        }
        t.localPosition = Vector3.zero;
        return t;
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
