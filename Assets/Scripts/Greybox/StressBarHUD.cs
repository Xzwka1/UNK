using UnityEngine;

/// <summary>
/// World-space horizontal Dual-Polarity stress gauge floating above the player's head, using the hand-drawn art
/// (Resources/UI/Stress_Bar_0 = empty frame, Resources/UI/Stress_Bar_100 = full colour).
/// The middle band is equilibrium (0%). Positive stress reveals the colour to the RIGHT (red / Tensile slime),
/// negative stress reveals it to the LEFT (purple / Brittle crystal).
/// </summary>
[RequireComponent(typeof(PlayerStress))]
public class StressBarHUD : MonoBehaviour
{
    [Header("Layout (world units)")]
    [Tooltip("Width/height of the whole gauge image in world units.")]
    [SerializeField] private float size = 2.0f;
    [Tooltip("Offset of the image centre from the player's pivot.")]
    [SerializeField] private Vector2 offset = new Vector2(0f, 1.6f);
    [SerializeField] private int sortingOrder = 20;
    [SerializeField] private string sortingLayerName = "UI";

    [Header("Art calibration (fractions of the image height, from the bottom)")]
    [Tooltip("Where the 0% equilibrium band sits.")]
    [SerializeField] private float zeroLine = 0.50f;
    [Tooltip("Distance from the zero line to the +100% / -100% end of the tube.")]
    [SerializeField] private float halfRange = 0.412f;

    [Header("Behaviour")]
    [Tooltip("Hide the gauge while stress is 0 and fade it in/out.")]
    [SerializeField] private bool hideWhenEmpty = false;
    [SerializeField] private float fadeSpeed = 6f;
    [SerializeField] private float fillSmoothing = 18f;
    [SerializeField] private float criticalPulseSpeed = 10f;
    [SerializeField] private float criticalPulseAmount = 0.04f;

    private PlayerStress stress;
    private Transform root, rig, maskT;
    private SpriteRenderer frameSr, fillSr;
    private SpriteMask mask;
    private float shown, alpha = 1f, imgScale = 1f;

    private void Awake()
    {
        stress = GetComponent<PlayerStress>();
        Build();
    }

    private static Sprite Load(string path)
    {
        var all = Resources.LoadAll<Sprite>(path);
        if (all != null && all.Length > 0) return all[0];
        Debug.LogWarning("[StressBarHUD] Missing sprite: Resources/" + path);
        return null;
    }

    private static Sprite whiteSprite;
    private static Sprite White()
    {
        if (whiteSprite != null) return whiteSprite;
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f); // exactly 1 unit
        return whiteSprite;
    }

    private static Material unlitMaterial;
    private static Material GetUnlitMaterial()
    {
        if (unlitMaterial != null) return unlitMaterial;
        Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (s == null) s = Shader.Find("Sprites/Default");
        if (s != null) unlitMaterial = new Material(s);
        return unlitMaterial;
    }

    private SpriteRenderer MakeSprite(string name, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(rig, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (!string.IsNullOrEmpty(sortingLayerName))
            sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = order;

        Material mat = GetUnlitMaterial();
        if (mat != null) sr.material = mat;

        if (sprite != null)
        {
            // Normalise: image becomes 'size' units wide and its centre sits on the root.
            imgScale = size / sprite.bounds.size.x;
            go.transform.localScale = Vector3.one * imgScale;
            go.transform.localPosition = -sprite.bounds.center * imgScale;
        }
        return sr;
    }

    private void Build()
    {
        root = new GameObject("StressGauge_Vertical").transform;
        root.SetParent(transform, false);
        root.localPosition = offset;

        // The art is drawn vertically (+ on top, - at the bottom). Rotate it -90 degrees so it lies horizontally:
        // LEFT = - (Brittle / crystal), RIGHT = + (Tensile / liquid).
        rig = new GameObject("Rig").transform;
        rig.SetParent(root, false);
        rig.localRotation = Quaternion.Euler(0f, 0f, -90f);

        frameSr = MakeSprite("Frame", Load("UI/Stress_Bar_0"), sortingOrder);
        fillSr = MakeSprite("FillColour", Load("UI/Stress_Bar_100"), sortingOrder + 1);
        fillSr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        var mgo = new GameObject("FillMask");
        mgo.transform.SetParent(rig, false);
        maskT = mgo.transform;
        mask = mgo.AddComponent<SpriteMask>();
        mask.sprite = White();
        mask.isCustomRangeActive = true;
        if (!string.IsNullOrEmpty(sortingLayerName))
        {
            int sLayerId = SortingLayer.NameToID(sortingLayerName);
            mask.frontSortingLayerID = sLayerId;
            mask.backSortingLayerID = sLayerId;
        }
        mask.frontSortingOrder = sortingOrder + 1;
        mask.backSortingOrder = sortingOrder;

        UpdateFill(0f);
    }

    private void UpdateFill(float signed)
    {
        float zeroY = (zeroLine - 0.5f) * size;
        float edgeY = zeroY + signed * halfRange * size;
        float height = Mathf.Max(0.0001f, Mathf.Abs(edgeY - zeroY));
        maskT.localPosition = new Vector3(0f, (zeroY + edgeY) * 0.5f, 0f);
        maskT.localScale = new Vector3(size, height, 1f);
    }

    private void LateUpdate()
    {
        if (stress == null) return;

        // Keep the gauge upright and un-mirrored even if the player root is flipped via scale.
        float sx = Mathf.Sign(transform.lossyScale.x);
        bool critical = stress.IsLiquidLocked || stress.IsBrittleLocked;
        float pulse = critical ? 1f + criticalPulseAmount * Mathf.Sin(Time.time * criticalPulseSpeed) : 1f;
        root.localScale = new Vector3(sx * pulse, pulse, 1f);

        float target = stress.SignedNormalized;
        shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-fillSmoothing * Time.deltaTime));
        UpdateFill(shown);

        float wantAlpha = (!hideWhenEmpty || Mathf.Abs(stress.CurrentStress) > 0.01f) ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, wantAlpha, fadeSpeed * Time.deltaTime);
        var c = new Color(1f, 1f, 1f, alpha);
        frameSr.color = c;
        fillSr.color = c;
    }
}
