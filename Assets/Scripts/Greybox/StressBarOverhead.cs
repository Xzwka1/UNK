using UnityEngine;

/// <summary>
/// World-space stress bar floating above the player's head (built from SpriteRenderers, no Canvas needed).
/// Fill colour follows the stress stage, two tick marks show the Stage 2 / Stage 3 thresholds,
/// and Stage 3 pulses. Added automatically by PlayerSetup; can also be added by hand.
/// </summary>
[RequireComponent(typeof(PlayerStress))]
public class StressBarOverhead : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Offset of the bar centre from the player's pivot (world units).")]
    [SerializeField] private Vector2 offset = new Vector2(0f, 1.1f);
    [SerializeField] private Vector2 size = new Vector2(1.2f, 0.16f);
    [SerializeField] private float border = 0.03f;
    [SerializeField] private int sortingOrder = 20;

    [Header("Behaviour")]
    [Tooltip("Hide the bar while stress is 0 and fade it in/out.")]
    [SerializeField] private bool hideWhenEmpty = true;
    [SerializeField] private float fadeSpeed = 6f;
    [Tooltip("How fast the fill follows the real value (higher = snappier).")]
    [SerializeField] private float fillSmoothing = 18f;
    [SerializeField] private float criticalPulseSpeed = 10f;

    [Header("Colours")]
    [SerializeField] private Color backColor = new Color(0.05f, 0.05f, 0.07f, 0.85f);
    [SerializeField] private Color stage1Color = new Color(0.19f, 0.82f, 0.35f);   // green
    [SerializeField] private Color stage2Color = new Color(1f, 0.62f, 0.04f);      // orange
    [SerializeField] private Color stage3Color = new Color(1f, 0.23f, 0.19f);      // red
    [SerializeField] private Color tickColor = new Color(1f, 1f, 1f, 0.8f);

    private PlayerStress stress;
    private Transform barRoot, fill;
    private SpriteRenderer back, fillSr, tick2, tick3;
    private float shown;      // smoothed 0..1
    private float alpha;

    private void Awake()
    {
        stress = GetComponent<PlayerStress>();
        Build();
    }

    private void Build()
    {
        Sprite sq = GreyboxSprites.Square;

        barRoot = new GameObject("StressBar").transform;
        barRoot.SetParent(transform, false);

        back = MakeRect("Back", sq, backColor, sortingOrder);
        fillSr = MakeRect("Fill", sq, stage1Color, sortingOrder + 1);
        fill = fillSr.transform;
        tick2 = MakeRect("Tick_Stage2", sq, tickColor, sortingOrder + 2);
        tick3 = MakeRect("Tick_Stage3", sq, tickColor, sortingOrder + 2);

        back.transform.localScale = new Vector3(size.x + border * 2f, size.y + border * 2f, 1f);
        tick2.transform.localScale = new Vector3(0.025f, size.y + border * 2f, 1f);
        tick3.transform.localScale = tick2.transform.localScale;
        PlaceTick(tick2.transform, stress.StageThreshold(StressStage.Stage2_Strained));
        PlaceTick(tick3.transform, stress.StageThreshold(StressStage.Stage3_Critical));
    }

    private SpriteRenderer MakeRect(string objName, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(barRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    private void PlaceTick(Transform t, float thresholdPercent)
    {
        float x = -size.x * 0.5f + size.x * Mathf.Clamp01(thresholdPercent / 100f);
        t.localPosition = new Vector3(x, 0f, 0f);
    }

    // LateUpdate so the bar follows the interpolated body without jitter.
    private void LateUpdate()
    {
        // Keep the bar upright and at a fixed world offset even if the player ever rotates/flips.
        barRoot.position = transform.position + (Vector3)offset;
        barRoot.rotation = Quaternion.identity;

        float target = stress.Normalized;
        shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-fillSmoothing * Time.deltaTime));
        if (Mathf.Abs(shown - target) < 0.001f) shown = target;

        // Fill grows from the left edge.
        float w = size.x * shown;
        fill.localScale = new Vector3(Mathf.Max(w, 0.0001f), size.y, 1f);
        fill.localPosition = new Vector3(-size.x * 0.5f + w * 0.5f, 0f, 0f);

        Color c = stress.Stage switch
        {
            StressStage.Stage3_Critical => stage3Color,
            StressStage.Stage2_Strained => stage2Color,
            _ => stage1Color
        };
        if (stress.Stage == StressStage.Stage3_Critical)
            c = Color.Lerp(c, Color.white, 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.time * criticalPulseSpeed)));

        float targetAlpha = (!hideWhenEmpty || target > 0.001f || shown > 0.001f) ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, targetAlpha, fadeSpeed * Time.deltaTime);

        Apply(back, backColor, alpha);
        Apply(fillSr, c, alpha);
        Apply(tick2, tickColor, alpha);
        Apply(tick3, tickColor, alpha);
    }

    private static void Apply(SpriteRenderer sr, Color baseColor, float a)
    {
        baseColor.a *= a;
        sr.color = baseColor;
    }
}
