using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main Menu. Drop this on an empty GameObject in a scene (the whole UI is built at runtime from
/// Resources/UI/Start and Resources/UI/Exit). Mouse, keyboard and gamepad navigation are supported.
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Flow")]
    [Tooltip("Scene loaded by the Start button (must be in Build Settings).")]
    [SerializeField] private string firstSceneName = "Map 1";


    [Header("Look")]
    [SerializeField] private Color backgroundTop = new Color(0.08f, 0.09f, 0.16f);
    [SerializeField] private Color backgroundBottom = new Color(0.02f, 0.02f, 0.05f);
    [SerializeField] private Color tensileGlow = new Color(1f, 0.45f, 0.1f, 0.22f);
    [SerializeField] private Color brittleGlow = new Color(0.45f, 0.3f, 1f, 0.25f);
    [SerializeField] private float buttonWidth = 480f;
    [SerializeField] private float buttonSpacing = 40f;

    private Button startButton;

    private void Start()
    {
        Time.timeScale = 1f;
        BuildEventSystem();
        BuildUI();
    }

    private void BuildEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    private static Sprite Load(string path)
    {
        var all = Resources.LoadAll<Sprite>(path);
        if (all != null && all.Length > 0) return all[0];
        Debug.LogWarning("[MainMenu] Missing sprite: Resources/" + path);
        return null;
    }

    private void BuildUI()
    {
        var canvasGo = new GameObject("MainMenuCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Background (vertical gradient built from two stacked images + polarity glows).
        var bg = MakeImage("Background", canvasGo.transform, Color.white);
        bg.sprite = GradientSprite(backgroundTop, backgroundBottom);
        Stretch(bg.rectTransform);
        MakeGlow("GlowTensile", canvasGo.transform, tensileGlow, new Vector2(-520f, 280f), 900f);
        MakeGlow("GlowBrittle", canvasGo.transform, brittleGlow, new Vector2(520f, -240f), 900f);

        // Buttons
        Sprite startSprite = Load("UI/Start");
        Sprite exitSprite = Load("UI/Exit");
        float aspect = startSprite != null ? startSprite.rect.height / startSprite.rect.width : 0.48f;
        float h = buttonWidth * aspect;

        startButton = MakeButton("StartButton", canvasGo.transform, startSprite, new Vector2(buttonWidth, h), new Vector2(0f, 20f));
        startButton.onClick.AddListener(OnStart);

        var exitButton = MakeButton("ExitButton", canvasGo.transform, exitSprite, new Vector2(buttonWidth, h),
            new Vector2(0f, 20f - h - buttonSpacing));
        exitButton.onClick.AddListener(OnExit);

        // Explicit keyboard / gamepad navigation.
        var nav = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = exitButton, selectOnUp = exitButton };
        startButton.navigation = nav;
        nav.selectOnDown = startButton; nav.selectOnUp = startButton;
        exitButton.navigation = nav;

        EventSystem.current.SetSelectedGameObject(startButton.gameObject);
    }

    private void OnStart() => SceneManager.LoadScene(firstSceneName);

    private void OnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ UI helpers
    private static Image MakeImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void MakeGlow(string name, Transform parent, Color color, Vector2 pos, float diameter)
    {
        var img = MakeImage(name, parent, color);
        img.sprite = RadialSprite();
        SetRect(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(diameter, diameter), pos);
    }

    private static Sprite GradientSprite(Color top, Color bottom)
    {
        const int n = 256;
        var tex = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(n - 1)));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, n), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite radial;
    private static Sprite RadialSprite()
    {
        if (radial != null) return radial;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        radial = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return radial;
    }

    private static Text MakeText(string name, Transform parent, string content, int size, FontStyle style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static Button MakeButton(string name, Transform parent, Sprite sprite, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), size, pos);

        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.selectedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;

        go.AddComponent<MenuButtonFX>();
        return btn;
    }

    private static void SetRect(RectTransform r, Vector2 anchor, Vector2 size, Vector2 pos)
    {
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = size;
        r.anchoredPosition = pos;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }
}

/// <summary>Hover / selected pop-scale for menu buttons.</summary>
public class MenuButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private bool hover, selected;
    private const float HoverScale = 1.08f;

    public void OnPointerEnter(PointerEventData e) => hover = true;
    public void OnPointerExit(PointerEventData e) => hover = false;
    public void OnSelect(BaseEventData e) => selected = true;
    public void OnDeselect(BaseEventData e) => selected = false;

    private void Update()
    {
        float target = (hover || selected) ? HoverScale : 1f;
        float s = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
        transform.localScale = new Vector3(s, s, 1f);
    }
}
