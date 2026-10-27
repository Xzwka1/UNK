using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// One-stop setup for the player character. Add this component to an empty GameObject
/// and it configures Rigidbody2D, CapsuleCollider2D, PlayerController2D, PlayerStress,
/// PlayerRespawn, PlayerHurtbox, PlayerAnimator2D, Overhead Stress Bar, and Visual scale.
/// </summary>
[RequireComponent(typeof(PlayerController2D), typeof(PlayerRespawn), typeof(PlayerStress))]
public class PlayerSetup : MonoBehaviour
{
    [Header("Pixel logic")]
    [SerializeField] private float pixelsPerUnit = 16f;
    [SerializeField] private Vector2 hitboxPixels = new Vector2(14f, 22f);
    [SerializeField] private Vector2 hurtboxPixels = new Vector2(10f, 18f);

    [Header("Character Art (Auto-loaded if empty)")]
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
            var setups = Object.FindObjectsByType<PlayerSetup>(FindObjectsInactive.Exclude);
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
        float halfHitboxHeight = (hitboxPixels.y / ppu) * 0.5f;

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
        if (runtime) Ensure<PlayerAudioBridge>(gameObject);
        if (runtime) _ = QuestBoxManager.Instance;

        // --- Ensure gameplay camera isn't zoomed out to 20 units.
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
            sr.sortingLayerName = "Entities";
            sr.sortingOrder = 10;

            bool hasCharacterArt = animatorController != null || defaultIdleSprite != null;
            if (hasCharacterArt)
            {
                if (Mathf.Approximately(characterVisualScale.x, 0.28f))
                {
                    characterVisualScale = new Vector3(0.35f, 0.35f, 1f);
                }

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

                Transform slimeTf = transform.Find("Slime Particle");
                if (slimeTf == null) slimeTf = transform.Find("Walking Slime Particle");
                ParticleSystem slimePs = slimeTf != null ? slimeTf.GetComponent<ParticleSystem>() : null;

                Transform dashTf = transform.Find("Dash Particle");
                if (dashTf == null) dashTf = transform.Find("Dash Slime VFX");
                ParticleSystem dashPs = dashTf != null ? dashTf.GetComponent<ParticleSystem>() : null;

                Transform dashTrailTf = transform.Find("DashTrail Slime VFX");
                if (dashTrailTf == null) dashTrailTf = transform.Find("DashTrail");
                ParticleSystem dashTrailPs = dashTrailTf != null ? dashTrailTf.GetComponent<ParticleSystem>() : null;

                Transform wjTf = transform.Find("WallJump Particle");
                if (wjTf == null) wjTf = transform.Find("Wall Jump Particle");
                if (wjTf == null) wjTf = transform.Find("WallJump VFX");
                ParticleSystem wjPs = wjTf != null ? wjTf.GetComponent<ParticleSystem>() : null;

                Transform deadTf = transform.Find("Dead Particle");
                if (deadTf == null) deadTf = transform.Find("Death Particle");
                ParticleSystem deadPs = deadTf != null ? deadTf.GetComponent<ParticleSystem>() : null;

                Transform wsTf = transform.Find("WallSlide Particle");
                if (wsTf == null) wsTf = transform.Find("Wall Slide Particle");
                if (wsTf == null) wsTf = transform.Find("WallSlide VFX");
                ParticleSystem wsPs = wsTf != null ? wsTf.GetComponent<ParticleSystem>() : null;

                var playerAnim = Ensure<PlayerAnimator2D>(gameObject);
                playerAnim.BindReferences(ctrl, stress, respawn, rb, anim, sr, overlaySr, slimePs, dashPs, dashTrailPs, wjPs, deadPs, wsPs);
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
