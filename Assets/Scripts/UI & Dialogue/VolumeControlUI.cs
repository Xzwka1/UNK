using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Volume Control UI using custom sprites:
/// - Speaker Icon (IMG_0524 / Resources/UI/VolumeIcon)
/// - Volume Bar   (IMG_0527 / Resources/UI/VolumeBar)
/// - Slider Knob  (IMG_0530 / Resources/UI/VolumeHandle)
///
/// Behavior:
/// - In MainMenu: Speaker icon is always visible at the bottom-left. Clicking the speaker icon
///   toggles the volume slider bar so the player can raise/lower volume.
/// - In Gameplay scenes: Pressing the 'M' key toggles the volume control UI (icon + slider bar) on/off.
/// </summary>
public class VolumeControlUI : MonoBehaviour
{
    public static VolumeControlUI Instance { get; private set; }

    [Header("Sprites (Auto-loaded if empty)")]
    [SerializeField] private Sprite speakerIconSprite;
    [SerializeField] private Sprite sliderBarSprite;
    [SerializeField] private Sprite sliderHandleSprite;

    [Header("Layout Settings (1920x1080 Reference)")]
    [SerializeField] private Vector2 bottomLeftOffset = new Vector2(95f, 80f);
    [SerializeField] private Vector2 iconSize = new Vector2(115f, 72f);
    [SerializeField] private Vector2 barSize = new Vector2(360f, 48f);
    [SerializeField] private Vector2 handleSize = new Vector2(22f, 68f);

    [Header("Gameplay Hotkey")]
    [SerializeField] private Key toggleKey = Key.M;

    private Canvas canvas;
    private GameObject rootContainer;
    private Button speakerButton;
    private Image speakerImage;
    private GameObject sliderContainer;
    private Slider volumeSlider;
    private Image fillTintImage;
    private Text percentText;
    private Text hintText;

    private bool isMainMenuScene;
    private bool isSliderOpen;
    private bool suppressSliderCallback;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        EnsureInstance();
    }

    public static VolumeControlUI EnsureInstance()
    {
        if (Instance != null) return Instance;

        VolumeControlUI existing = FindFirstObjectByType<VolumeControlUI>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject go = new GameObject("VolumeControlUI (Persistent)");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<VolumeControlUI>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureSpritesLoaded();
        BuildUI();
        ApplySceneState(SceneManager.GetActiveScene());
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplySceneState(scene);
    }

    private void Update()
    {
        bool mPressed = false;
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            mPressed = true;
        }
        else if (Input.GetKeyDown(KeyCode.M))
        {
            mPressed = true;
        }

        if (mPressed)
        {
            ToggleVolumeUI();
        }
    }

    private void ApplySceneState(Scene scene)
    {
        isMainMenuScene = scene.name == "MainMenu";

        if (rootContainer == null)
        {
            BuildUI();
        }

        float currentVol = SoundManager.Instance != null ? SoundManager.Instance.MasterVolume : AudioListener.volume;
        SyncSliderValue(currentVol);

        if (isMainMenuScene)
        {
            // In MainMenu: Speaker icon at bottom-left is always visible; slider opens when clicking the icon.
            EnsureEventSystem();
            rootContainer.SetActive(true);
            SetSliderOpen(false, playSound: false);
            if (hintText != null)
            {
                hintText.text = "CLICK ICON TO ADJUST VOLUME";
                hintText.gameObject.SetActive(false);
            }
        }
        else
        {
            // In Gameplay scenes: Hidden until player presses M.
            rootContainer.SetActive(false);
            isSliderOpen = false;
            if (sliderContainer != null)
            {
                sliderContainer.SetActive(false);
            }
            if (hintText != null)
            {
                hintText.text = "[M] VOLUME";
                hintText.gameObject.SetActive(true);
            }
        }
    }

    public void ToggleVolumeUI()
    {
        EnsureEventSystem();

        if (isMainMenuScene)
        {
            rootContainer.SetActive(true);
            SetSliderOpen(!isSliderOpen, playSound: true);
        }
        else
        {
            bool nextState = !rootContainer.activeSelf;
            rootContainer.SetActive(nextState);
            SetSliderOpen(nextState, playSound: true);
        }
    }

    private void OnSpeakerIconClicked()
    {
        EnsureEventSystem();

        if (isMainMenuScene)
        {
            // In MainMenu, clicking the bottom-left speaker icon opens/closes the volume slider bar.
            SetSliderOpen(!isSliderOpen, playSound: true);
        }
        else
        {
            // In Gameplay (when already opened via M), clicking the speaker icon toggles the slider or closes it.
            SetSliderOpen(!isSliderOpen, playSound: true);
        }
    }

    private void SetSliderOpen(bool open, bool playSound)
    {
        isSliderOpen = open;
        if (sliderContainer != null)
        {
            sliderContainer.SetActive(open);
        }

        if (open)
        {
            float currentVol = SoundManager.Instance != null ? SoundManager.Instance.MasterVolume : AudioListener.volume;
            SyncSliderValue(currentVol);
        }

        if (playSound)
        {
            SoundManager.PlayButtonClick();
        }
    }

    private void OnSliderValueChanged(float value)
    {
        if (suppressSliderCallback) return;

        float clamped = Mathf.Clamp01(value);
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.MasterVolume = clamped;
        }
        AudioListener.volume = clamped;

        UpdateVisualFeedback(clamped);
    }

    private void SyncSliderValue(float volume)
    {
        suppressSliderCallback = true;
        if (volumeSlider != null)
        {
            volumeSlider.value = Mathf.Clamp01(volume);
        }
        UpdateVisualFeedback(Mathf.Clamp01(volume));
        suppressSliderCallback = false;
    }

    private void UpdateVisualFeedback(float volume)
    {
        int pct = Mathf.RoundToInt(volume * 100f);
        if (percentText != null)
        {
            percentText.text = pct + "%";
        }

        if (speakerImage != null)
        {
            // Dim the speaker slightly when muted (0%)
            speakerImage.color = volume <= 0.001f
                ? new Color(0.55f, 0.55f, 0.55f, 0.85f)
                : Color.white;
        }

        if (fillTintImage != null)
        {
            fillTintImage.color = new Color(0.45f, 0.9f, 1f, 0.25f);
        }
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        GameObject esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    private void EnsureSpritesLoaded()
    {
        if (speakerIconSprite == null) speakerIconSprite = LoadSprite("UI/VolumeIcon", "Assets/UI_Interface/IMG_0524.png");
        if (sliderBarSprite == null) sliderBarSprite = LoadSprite("UI/VolumeBar", "Assets/UI_Interface/IMG_0527.png");
        if (sliderHandleSprite == null) sliderHandleSprite = LoadSprite("UI/VolumeHandle", "Assets/UI_Interface/IMG_0530.png");
    }

    private static Sprite LoadSprite(string resourcePath, string editorAssetPath)
    {
        Sprite[] all = Resources.LoadAll<Sprite>(resourcePath);
        if (all != null && all.Length > 0) return all[0];

        Sprite single = Resources.Load<Sprite>(resourcePath);
        if (single != null) return single;

#if UNITY_EDITOR
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(editorAssetPath);
        if (assets != null)
        {
            foreach (Object obj in assets)
            {
                if (obj is Sprite s) return s;
            }
        }
#endif
        return null;
    }

    private void BuildUI()
    {
        if (canvas != null) return;

        // 1. Dedicated Overlay Canvas
        GameObject canvasGo = new GameObject("VolumeControl_Canvas");
        canvasGo.transform.SetParent(transform, false);

        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95; // Above MainMenu and HUD

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // 2. Bottom-Left Root Container
        rootContainer = new GameObject("BottomLeft_VolumeWidget", typeof(RectTransform));
        rootContainer.transform.SetParent(canvasGo.transform, false);
        RectTransform rootRect = rootContainer.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.zero;
        rootRect.pivot = new Vector2(0f, 0.5f);
        rootRect.anchoredPosition = new Vector2(bottomLeftOffset.x - iconSize.x * 0.5f, bottomLeftOffset.y);
        rootRect.sizeDelta = new Vector2(iconSize.x + barSize.x + 140f, Mathf.Max(iconSize.y, handleSize.y) + 40f);

        // 3. Speaker Icon Button (Bottom-Left)
        GameObject speakerGo = new GameObject("SpeakerIconButton", typeof(RectTransform));
        speakerGo.transform.SetParent(rootContainer.transform, false);
        RectTransform speakerRect = speakerGo.GetComponent<RectTransform>();
        speakerRect.anchorMin = new Vector2(0f, 0.5f);
        speakerRect.anchorMax = new Vector2(0f, 0.5f);
        speakerRect.pivot = new Vector2(0.5f, 0.5f);
        speakerRect.anchoredPosition = new Vector2(iconSize.x * 0.5f, 0f);
        speakerRect.sizeDelta = iconSize;

        speakerImage = speakerGo.AddComponent<Image>();
        speakerImage.sprite = speakerIconSprite;
        speakerImage.preserveAspect = true;
        speakerImage.raycastTarget = true;

        speakerButton = speakerGo.AddComponent<Button>();
        speakerButton.targetGraphic = speakerImage;
        ColorBlock cb = speakerButton.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.selectedColor = Color.white;
        speakerButton.colors = cb;
        speakerButton.navigation = new Navigation { mode = Navigation.Mode.None };
        speakerButton.onClick.AddListener(OnSpeakerIconClicked);

        speakerGo.AddComponent<MenuButtonFX>();

        // 4. Expandable Slider Container (To the right of Speaker Icon)
        sliderContainer = new GameObject("VolumeSliderContainer", typeof(RectTransform));
        sliderContainer.transform.SetParent(rootContainer.transform, false);
        RectTransform sliderContainerRect = sliderContainer.GetComponent<RectTransform>();
        sliderContainerRect.anchorMin = new Vector2(0f, 0.5f);
        sliderContainerRect.anchorMax = new Vector2(0f, 0.5f);
        sliderContainerRect.pivot = new Vector2(0f, 0.5f);
        sliderContainerRect.anchoredPosition = new Vector2(iconSize.x + 22f, 0f);
        sliderContainerRect.sizeDelta = new Vector2(barSize.x + 110f, Mathf.Max(barSize.y, handleSize.y) + 20f);

        // Soft dark backing behind the slider + percentage for readability in bright/snow maps
        GameObject backdropGo = new GameObject("SliderBackdrop", typeof(RectTransform));
        backdropGo.transform.SetParent(sliderContainer.transform, false);
        RectTransform backdropRect = backdropGo.GetComponent<RectTransform>();
        backdropRect.anchorMin = new Vector2(0f, 0.5f);
        backdropRect.anchorMax = new Vector2(0f, 0.5f);
        backdropRect.pivot = new Vector2(0f, 0.5f);
        backdropRect.anchoredPosition = new Vector2(-12f, 0f);
        backdropRect.sizeDelta = new Vector2(barSize.x + 115f, Mathf.Max(barSize.y, handleSize.y) + 16f);
        Image backdropImg = backdropGo.AddComponent<Image>();
        backdropImg.color = new Color(0.03f, 0.04f, 0.08f, 0.65f);
        backdropImg.raycastTarget = false;

        // 5. Unity Slider (using IMG_0527 bar & IMG_0530 handle)
        GameObject sliderGo = new GameObject("VolumeSlider", typeof(RectTransform));
        sliderGo.transform.SetParent(sliderContainer.transform, false);
        RectTransform sliderRect = sliderGo.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0.5f);
        sliderRect.anchorMax = new Vector2(0f, 0.5f);
        sliderRect.pivot = new Vector2(0f, 0.5f);
        sliderRect.anchoredPosition = Vector2.zero;
        sliderRect.sizeDelta = new Vector2(barSize.x, Mathf.Max(barSize.y, handleSize.y));

        // Background Bar Image (IMG_0527)
        GameObject barBgGo = new GameObject("BarBackground", typeof(RectTransform));
        barBgGo.transform.SetParent(sliderGo.transform, false);
        RectTransform barBgRect = barBgGo.GetComponent<RectTransform>();
        barBgRect.anchorMin = new Vector2(0f, 0.5f);
        barBgRect.anchorMax = new Vector2(1f, 0.5f);
        barBgRect.pivot = new Vector2(0.5f, 0.5f);
        barBgRect.anchoredPosition = Vector2.zero;
        barBgRect.sizeDelta = new Vector2(0f, barSize.y);

        Image barBgImage = barBgGo.AddComponent<Image>();
        barBgImage.sprite = sliderBarSprite;
        barBgImage.preserveAspect = false;
        barBgImage.raycastTarget = true;

        // Fill Area (subtle cyan highlight up to handle)
        GameObject fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaGo.transform.SetParent(sliderGo.transform, false);
        RectTransform fillAreaRect = fillAreaGo.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.pivot = new Vector2(0.5f, 0.5f);
        fillAreaRect.anchoredPosition = Vector2.zero;
        fillAreaRect.sizeDelta = new Vector2(-24f, barSize.y * 0.42f);

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(fillAreaGo.transform, false);
        RectTransform fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillTintImage = fillGo.AddComponent<Image>();
        fillTintImage.color = new Color(0.45f, 0.9f, 1f, 0.25f);
        fillTintImage.raycastTarget = false;

        // Handle Slide Area (inset slightly so handle stays inside the rounded bar ends)
        GameObject handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleAreaGo.transform.SetParent(sliderGo.transform, false);
        RectTransform handleAreaRect = handleAreaGo.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(16f, 0f);
        handleAreaRect.offsetMax = new Vector2(-16f, 0f);

        GameObject handleGo = new GameObject("Handle", typeof(RectTransform));
        handleGo.transform.SetParent(handleAreaGo.transform, false);
        RectTransform handleRect = handleGo.GetComponent<RectTransform>();
        handleRect.sizeDelta = handleSize;

        Image handleImage = handleGo.AddComponent<Image>();
        handleImage.sprite = sliderHandleSprite;
        handleImage.preserveAspect = true;
        handleImage.raycastTarget = true;

        volumeSlider = sliderGo.AddComponent<Slider>();
        volumeSlider.direction = Slider.Direction.LeftToRight;
        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.wholeNumbers = false;
        volumeSlider.targetGraphic = handleImage;
        volumeSlider.fillRect = fillRect;
        volumeSlider.handleRect = handleRect;
        volumeSlider.onValueChanged.AddListener(OnSliderValueChanged);

        // 6. Percentage Readout Text
        GameObject pctGo = new GameObject("VolumePercentText", typeof(RectTransform));
        pctGo.transform.SetParent(sliderContainer.transform, false);
        RectTransform pctRect = pctGo.GetComponent<RectTransform>();
        pctRect.anchorMin = new Vector2(0f, 0.5f);
        pctRect.anchorMax = new Vector2(0f, 0.5f);
        pctRect.pivot = new Vector2(0f, 0.5f);
        pctRect.anchoredPosition = new Vector2(barSize.x + 14f, 0f);
        pctRect.sizeDelta = new Vector2(85f, 44f);

        percentText = pctGo.AddComponent<Text>();
        percentText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        percentText.text = "100%";
        percentText.fontSize = 26;
        percentText.fontStyle = FontStyle.Bold;
        percentText.color = Color.white;
        percentText.alignment = TextAnchor.MiddleLeft;
        percentText.raycastTarget = false;

        Shadow pctShadow = pctGo.AddComponent<Shadow>();
        pctShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        pctShadow.effectDistance = new Vector2(2f, -2f);

        // 7. Gameplay Hint Label ("[M] VOLUME") above the bar
        GameObject hintGo = new GameObject("VolumeHintText", typeof(RectTransform));
        hintGo.transform.SetParent(sliderContainer.transform, false);
        RectTransform hintRect = hintGo.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 1f);
        hintRect.anchorMax = new Vector2(0f, 1f);
        hintRect.pivot = new Vector2(0f, 0f);
        hintRect.anchoredPosition = new Vector2(4f, 2f);
        hintRect.sizeDelta = new Vector2(300f, 28f);

        hintText = hintGo.AddComponent<Text>();
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hintText.text = "[M] VOLUME";
        hintText.fontSize = 18;
        hintText.fontStyle = FontStyle.Bold;
        hintText.color = new Color(0.85f, 0.92f, 1f, 0.9f);
        hintText.alignment = TextAnchor.LowerLeft;
        hintText.raycastTarget = false;

        Shadow hintShadow = hintGo.AddComponent<Shadow>();
        hintShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        hintShadow.effectDistance = new Vector2(1.5f, -1.5f);
    }
}
