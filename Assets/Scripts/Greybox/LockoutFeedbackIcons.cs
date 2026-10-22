using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player feedback for the critical (±80%) stress lockouts, drawn on screen:
///   - Bottom-left: a row of "forbidden action" icons while locked
///     (+80% Tensile: No Dash / No Double Jump / No Climb, -80% Brittle: No Wall Grip / No Wall Jump / Heavy Fall danger).
///     When the player actually tries a denied action, that icon pops big and shakes.
///   - Top-center: short explanation text (what is locked + how to recover).
/// Icons are loaded from Resources/LockoutIcons (PNG, transparent).
/// </summary>
[RequireComponent(typeof(PlayerStress))]
public class LockoutFeedbackIcons : MonoBehaviour
{
    [Header("Icons (bottom-left, reference resolution 1920x1080)")]
    [SerializeField] private Vector2 margin = new Vector2(40f, 40f);
    [SerializeField] private float iconSize = 96f;
    [SerializeField] private float spacing = 110f;
    [SerializeField] private int sortingOrder = 60;

    [Header("Denied Pop")]
    [SerializeField] private float popDuration = 0.5f;
    [SerializeField] private float popScale = 1.6f;
    [SerializeField] private float shakeAmount = 8f;

    [Header("Behaviour")]
    [SerializeField] private float fadeSpeed = 8f;
    [SerializeField] private float pulseSpeed = 8f;

    [Header("Explanation Text (top-center)")]
    [SerializeField] private float textDuration = 1.3f;
    [SerializeField] private float textTopMargin = 60f;
    [SerializeField] private int titleFontSize = 56;
    [SerializeField] private int hintFontSize = 34;

    private static readonly string[] IconNames =
    {
        "icon_no_dash", "icon_no_double_jump", "icon_no_climb",
        "icon_no_wall_grip", "icon_no_wall_jump", "icon_danger_heavy_fall"
    };
    private const int Count = 6;

    private static readonly string[] Titles =
    {
        "DASH LOCKED", "AIR JUMP LOCKED", "CLIMB FAILED",
        "NO WALL GRIP", "WALL JUMP LOCKED", "SHATTER RISK"
    };
    private static readonly string[] Hints =
    {
        "Stress too high - land to cool down", "Stress too high - land to cool down", "Too fluid - land to cool down",
        "Too brittle - land to recover", "Too brittle - land to recover", "Don't fall far - recover on ground"
    };
    private static readonly Color TensileText = new Color(1f, 0.6f, 0.2f);
    private static readonly Color BrittleText = new Color(0.55f, 0.9f, 1f);

    private PlayerStress stress;
    private GameObject canvasGo;
    private readonly Image[] icons = new Image[Count];
    private readonly RectTransform[] rects = new RectTransform[Count];
    private readonly float[] popTimer = new float[Count];
    private readonly float[] alpha = new float[Count];
    private Text titleText, hintText;
    private CanvasGroup textGroup;
    private float textTimer;

    private void Awake()
    {
        stress = GetComponent<PlayerStress>();
        Build();
    }

    private void OnEnable()
    {
        if (stress == null) stress = GetComponent<PlayerStress>();
        stress.LockoutDenied += OnDenied;
    }

    private void OnDisable()
    {
        if (stress != null) stress.LockoutDenied -= OnDenied;
    }

    private void OnDestroy()
    {
        if (canvasGo != null) Destroy(canvasGo);
    }

    private void Build()
    {
        canvasGo = new GameObject("LockoutFeedback_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // --- Icons, bottom-left.
        for (int i = 0; i < Count; i++)
        {
            var go = new GameObject(IconNames[i], typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(iconSize, iconSize);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            var tex = Resources.Load<Texture2D>("LockoutIcons/" + IconNames[i]);
            if (tex != null)
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            else
                Debug.LogWarning("[LockoutFeedbackIcons] Missing icon: Resources/LockoutIcons/" + IconNames[i]);
            img.color = new Color(1f, 1f, 1f, 0f);
            icons[i] = img;
            rects[i] = rt;
        }

        // --- Explanation text, top-center.
        var tgo = new GameObject("LockoutText", typeof(RectTransform), typeof(CanvasGroup));
        tgo.transform.SetParent(canvasGo.transform, false);
        var trt = (RectTransform)tgo.transform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.sizeDelta = new Vector2(1400f, 150f);
        trt.anchoredPosition = new Vector2(0f, -textTopMargin);
        textGroup = tgo.GetComponent<CanvasGroup>();
        textGroup.alpha = 0f;
        textGroup.blocksRaycasts = false;
        textGroup.interactable = false;

        titleText = MakeText("Title", trt, titleFontSize, FontStyle.Bold, 0f, 80f);
        hintText = MakeText("Hint", trt, hintFontSize, FontStyle.Normal, -80f, 60f);
    }

    private static Text MakeText(string name, Transform parent, int size, FontStyle style, float y, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(1400f, height);
        rt.anchoredPosition = new Vector2(0f, y);

        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.UpperCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        var o = go.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.85f);
        o.effectDistance = new Vector2(3f, -3f);
        return t;
    }

    private void OnDenied(LockoutKind kind)
    {
        int i = (int)kind;
        popTimer[i] = popDuration;
        textTimer = textDuration;
        titleText.text = Titles[i];
        hintText.text = Hints[i];
        Color c = i <= (int)LockoutKind.NoClimb ? TensileText : BrittleText;
        titleText.color = c;
        hintText.color = Color.white;
    }

    private bool IsPersistent(int i)
    {
        // Heavy Fall is a warning only meaningful while brittle-locked.
        if (i <= (int)LockoutKind.NoClimb) return stress.IsLiquidLocked;
        return stress.IsBrittleLocked;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        int liquidSlot = 0, brittleSlot = 0;
        for (int i = 0; i < Count; i++)
        {
            bool persistent = IsPersistent(i);
            popTimer[i] = Mathf.Max(0f, popTimer[i] - dt);
            bool popping = popTimer[i] > 0f;

            float target = (persistent || popping) ? 1f : 0f;
            alpha[i] = Mathf.MoveTowards(alpha[i], target, fadeSpeed * dt);

            int slot = i <= (int)LockoutKind.NoClimb ? liquidSlot++ : brittleSlot++;
            Vector2 pos = new Vector2(margin.x + iconSize * 0.5f + slot * spacing, margin.y + iconSize * 0.5f);

            float scale = 1f;
            if (persistent) scale += 0.06f * Mathf.Sin(Time.unscaledTime * pulseSpeed);
            if (popping)
            {
                float k = popTimer[i] / popDuration; // 1 -> 0
                scale = Mathf.Lerp(1f, popScale, Mathf.Sin(k * Mathf.PI * 0.5f));
                pos.x += Mathf.Sin(Time.unscaledTime * 60f) * shakeAmount * k;
                pos.y += 10f * k;
            }

            rects[i].anchoredPosition = pos;
            rects[i].localScale = Vector3.one * scale;
            icons[i].color = new Color(1f, 1f, 1f, alpha[i]);
        }

        textTimer = Mathf.Max(0f, textTimer - dt);
        textGroup.alpha = Mathf.Clamp01(textTimer / 0.3f);
    }
}
