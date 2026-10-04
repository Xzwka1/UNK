using UnityEngine;

/// <summary>
/// One-stop physics setup for the player. Add this single component to an empty GameObject and it pulls in
/// everything else via RequireComponent (Rigidbody2D, CapsuleCollider2D, controller, stress, respawn), then:
///  - sizes the Hitbox (CapsuleCollider2D, 14x22 px) and gives it a zero-friction material,
///  - creates a child "Hurtbox" trigger (10x18 px) used for hazards,
///  - creates a simple grey/blue "Visual" rectangle.
/// Runs in Reset (when added), Awake, and from the context menu "Apply Setup".
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D), typeof(PlayerController2D),
                  typeof(PlayerStress), typeof(PlayerRespawn))]
public class PlayerSetup : MonoBehaviour
{
    [Header("Pixel logic")]
    [SerializeField] private float pixelsPerUnit = 16f;
    [SerializeField] private Vector2 hitboxPixels = new Vector2(14f, 22f);
    [SerializeField] private Vector2 hurtboxPixels = new Vector2(10f, 18f);

    [Header("Greybox visual")]
    [SerializeField] private bool createVisual = true;
    [SerializeField] private Color visualColor = new Color(0.25f, 0.55f, 1f);

    private const string HurtboxName = "Hurtbox";
    private const string VisualName = "Visual";

    private void Reset() => Apply(false);
    private void Awake() => Apply(true);

    [ContextMenu("Apply Setup")]
    private void ApplyFromMenu() => Apply(Application.isPlaying);

    private void Apply(bool runtime)
    {
        float ppu = Mathf.Max(1f, pixelsPerUnit);

        // --- Rigidbody: no rotation, smooth + safe at dash speed.
        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

        // --- Hitbox: capsule, so corners round off instead of snagging.
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

        // --- Visual (greybox rectangle, scaled to the hitbox).
        if (createVisual)
        {
            Transform vis = FindOrCreateChild(VisualName);
            vis.localScale = new Vector3(hitboxPixels.x / ppu, hitboxPixels.y / ppu, 1f);
            var sr = Ensure<SpriteRenderer>(vis.gameObject);
            sr.color = visualColor;
            sr.sortingOrder = 10;
            if (runtime) sr.sprite = GreyboxSprites.Square;
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
