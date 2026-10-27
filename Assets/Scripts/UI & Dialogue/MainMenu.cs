using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main Menu. Builds the Main Menu UI from Resources/UI/MainMenuScreen, Resources/UI/Start,
/// and Resources/UI/Exit, with START and EXIT buttons positioned on the left notepad as designed.
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Flow")]
    [Tooltip("Scene loaded by the Start button (must be in Build Settings).")]
    [SerializeField] private string firstSceneName = "Map 1";

    [Header("Layout (1920x1080 Reference)")]
    [SerializeField] private float menuButtonWidth = 318f;
    [SerializeField] private Vector2 startButtonPos = new Vector2(-517f, 190f);
    [SerializeField] private Vector2 exitButtonPos = new Vector2(-517f, -20f);

    [Header("Fallback Look (if MainMenuScreen sprite is missing)")]
    [SerializeField] private Color backgroundTop = new Color(0.08f, 0.09f, 0.16f);
    [SerializeField] private Color backgroundBottom = new Color(0.02f, 0.02f, 0.05f);

    private Button startButton;

    private void Start()
    {
        Time.timeScale = 1f;
        BuildEventSystem();
        BuildUI();
        VolumeControlUI.EnsureInstance();
        SoundManager.PlayMainMenuMusic();
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
        var single = Resources.Load<Sprite>(path);
        if (single != null) return single;
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

        // Background artwork (Resources/UI/MainMenuScreen)
        var bg = MakeImage("Background", canvasGo.transform, Color.white);
        Sprite bgSprite = Load("UI/MainMenuScreen");
        if (bgSprite != null)
        {
            bg.sprite = bgSprite;
            bg.preserveAspect = false;
        }
        else
        {
            bg.sprite = GradientSprite(backgroundTop, backgroundBottom);
        }
        Stretch(bg.rectTransform);

        // START and EXIT Buttons positioned inside the notepad on the left side
        Sprite startSprite = Load("UI/Start");
        Sprite exitSprite = Load("UI/Exit");
        float aspect = startSprite != null ? startSprite.rect.height / startSprite.rect.width : 0.481f;
        float w = menuButtonWidth > 10f ? menuButtonWidth : 318f;
        float h = w * aspect;

        startButton = MakeButton("StartButton", canvasGo.transform, startSprite, new Vector2(w, h), startButtonPos);
        startButton.onClick.AddListener(OnStart);

        var exitButton = MakeButton("ExitButton", canvasGo.transform, exitSprite, new Vector2(w, h), exitButtonPos);
        exitButton.onClick.AddListener(OnExit);

        // Explicit keyboard / gamepad navigation.
        var nav = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = exitButton, selectOnUp = exitButton };
        startButton.navigation = nav;
        nav.selectOnDown = startButton;
        nav.selectOnUp = startButton;
        exitButton.navigation = nav;

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(startButton.gameObject);
        }
    }

    private void OnStart()
    {
        SoundManager.PlayButtonClick();
        SceneManager.LoadScene(firstSceneName);
    }

    private void OnExit()
    {
        SoundManager.PlayButtonClick();
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

    private static Sprite GradientSprite(Color top, Color bottom)
    {
        const int n = 256;
        var tex = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(n - 1)));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, n), new Vector2(0.5f, 0.5f), 100f);
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
