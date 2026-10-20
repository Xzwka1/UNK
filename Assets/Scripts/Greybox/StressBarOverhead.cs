using UnityEngine;

/// <summary>
/// Dual-Polarity world-space stress bar floating above the player's head (-100% Brittle <-> 0 <-> +100% Tensile).
/// Center (x = 0) is equilibrium:
///   - Positive stress (+1% to +100%) fills to the RIGHT (warm orange/red).
///   - Negative stress (-1% to -100%) fills to the LEFT (icy cyan/blue).
///   - Tick marks show the ±50% Strained and ±80% Critical Lockout zones.
/// </summary>
[RequireComponent(typeof(PlayerStress))]
public class StressBarOverhead : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Offset of the bar centre from the player's pivot (world units).")]
    [SerializeField] private Vector2 offset = new Vector2(0f, 1.2f);
    [SerializeField] private Vector2 size = new Vector2(1.4f, 0.16f);
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
    [SerializeField] private Color centerTickColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color tickColor = new Color(1f, 1f, 1f, 0.55f);

    [Header("Tensile (+ Liquid) Colours")]
    [SerializeField] private Color tensileNormalColor = new Color(1f, 0.62f, 0.04f);   // orange
    [SerializeField] private Color tensileCriticalColor = new Color(1f, 0.23f, 0.19f); // red

    [Header("Brittle (- Crystal) Colours")]
    [SerializeField] private Color brittleNormalColor = new Color(0.20f, 0.78f, 0.95f);   // cyan
    [SerializeField] private Color brittleCriticalColor = new Color(0.55f, 0.35f, 1f);    // deep violet-ice

    private PlayerStress stress;
    private Transform barRoot, fill;
    private SpriteRenderer back, fillSr, centerTick, tickPos50, tickPos80, tickNeg50, tickNeg80;
    private float shownSigned; // smoothed -1..+1
    private float alpha;

    private void Awake()
    {
        stress = GetComponent<PlayerStress>();
        Build();
    }

    private void Build()
    {
        Sprite sq = GreyboxSprites.Square;

        barRoot = new GameObject("StressBar_DualPolarity").transform;
        barRoot.SetParent(transform, false);

        back = MakeRect("Back", sq, backColor, sortingOrder);
        fillSr = MakeRect("Fill", sq, tensileNormalColor, sortingOrder + 1);
        fill = fillSr.transform;

        centerTick = MakeRect("Tick_Center0", sq, centerTickColor, sortingOrder + 2);
        tickPos50 = MakeRect("Tick_Pos50", sq, tickColor, sortingOrder + 2);
        tickPos80 = MakeRect("Tick_Pos80", sq, tickColor, sortingOrder + 2);
        tickNeg50 = MakeRect("Tick_Neg50", sq, tickColor, sortingOrder + 2);
        tickNeg80 = MakeRect("Tick_Neg80", sq, tickColor, sortingOrder + 2);

        back.transform.localScale = new Vector3(size.x + border * 2f, size.y + border * 2f, 1f);
        Vector3 tickScale = new Vector3(0.02f, size.y + border * 2f, 1f);
        centerTick.transform.localScale = new Vector3(0.03f, size.y + border * 2.5f, 1f);
        tickPos50.transform.localScale = tickScale;
        tickPos80.transform.localScale = tickScale;
        tickNeg50.transform.localScale = tickScale;
        tickNeg80.transform.localScale = tickScale;

        PlaceSignedTick(centerTick.transform, 0f);
        PlaceSignedTick(tickPos50.transform, stress.StrainedThreshold);
        PlaceSignedTick(tickPos80.transform, stress.CriticalThreshold);
        PlaceSignedTick(tickNeg50.transform, -stress.StrainedThreshold);
        PlaceSignedTick(tickNeg80.transform, -stress.CriticalThreshold);
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

    private void PlaceSignedTick(Transform t, float signedPercent)
    {
        float halfW = size.x * 0.5f;
        float x = Mathf.Clamp(signedPercent / 100f, -1f, 1f) * halfW;
        t.localPosition = new Vector3(x, 0f, 0f);
    }

    private void LateUpdate()
    {
        barRoot.position = transform.position + (Vector3)offset;
        barRoot.rotation = Quaternion.identity;

        float target = stress.SignedNormalized; // -1..+1
        shownSigned = Mathf.Lerp(shownSigned, target, 1f - Mathf.Exp(-fillSmoothing * Time.deltaTime));
        if (Mathf.Abs(shownSigned - target) < 0.001f) shownSigned = target;

        float halfW = size.x * 0.5f;
        float w = Mathf.Abs(shownSigned) * halfW;
        fill.localScale = new Vector3(Mathf.Max(w, 0.0001f), size.y, 1f);
        fill.localPosition = new Vector3(Mathf.Sign(shownSigned) * w * 0.5f, 0f, 0f);

        Color c;
        if (shownSigned >= 0f)
        {
            c = stress.IsLiquidLocked ? tensileCriticalColor : tensileNormalColor;
        }
        else
        {
            c = stress.IsBrittleLocked ? brittleCriticalColor : brittleNormalColor;
        }

        if (stress.IsLiquidLocked || stress.IsBrittleLocked)
            c = Color.Lerp(c, Color.white, 0.3f * (0.5f + 0.5f * Mathf.Sin(Time.time * criticalPulseSpeed)));

        float targetAlpha = (!hideWhenEmpty || Mathf.Abs(target) > 0.001f || Mathf.Abs(shownSigned) > 0.001f) ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, targetAlpha, fadeSpeed * Time.deltaTime);

        Apply(back, backColor, alpha);
        Apply(fillSr, c, alpha);
        Apply(centerTick, centerTickColor, alpha);
        Apply(tickPos50, tickColor, alpha);
        Apply(tickPos80, tickColor, alpha);
        Apply(tickNeg50, tickColor, alpha);
        Apply(tickNeg80, tickColor, alpha);
    }

    private static void Apply(SpriteRenderer sr, Color baseColor, float a)
    {
        baseColor.a *= a;
        sr.color = baseColor;
    }
}
