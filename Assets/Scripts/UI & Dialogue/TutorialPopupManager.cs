using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Renders the on-screen Tutorial Book popup when the player enters a <see cref="TutorialTrigger2D"/>.
/// Freezes gameplay (Time.timeScale = 0) while displayed and waits for the player to press SPACE to continue.
/// </summary>
public class TutorialPopupManager : MonoBehaviour
{
    private static TutorialPopupManager instance;
    private static bool isQuitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
        isQuitting = false;
    }

    public static TutorialPopupManager Instance
    {
        get
        {
            if (isQuitting || !Application.isPlaying) return instance;
            if (instance == null)
            {
                instance = FindFirstObjectByType<TutorialPopupManager>();
                if (instance == null)
                {
                    var go = new GameObject("TutorialPopupManager");
                    instance = go.AddComponent<TutorialPopupManager>();
                }
            }
            return instance;
        }
    }

    [Header("Canvas Settings (1920x1080 reference)")]
    [SerializeField] private int sortingOrder = 70;
    [SerializeField] private float fadeInSpeed = 10f;
    [SerializeField] private float fadeOutSpeed = 8f;
    [SerializeField] private float popDuration = 0.22f;
    [Tooltip("Minimum unscaled seconds before SPACE can dismiss the popup (prevents accidental skip when jumping into trigger).")]
    [SerializeField] private float minHoldBeforeDismiss = 0.2f;

    private GameObject canvasGo;
    private CanvasGroup rootGroup;
    private Image backdropImage;
    private RectTransform imageRect;
    private Image tutorialImage;
    private RectTransform promptRect;
    private Text promptText;

    private TutorialTrigger2D activeTrigger;
    private bool isShowing;
    private bool isFrozenByPopup;
    private bool unfreezeNextFrame;
    private float openedUnscaledTime;
    private float popTimer;
    private float hideTimer = -1f;

    private Vector2 targetSize = new Vector2(880f, 660f);
    private Vector2 targetPosition = new Vector2(0f, 50f);

    public bool IsShowing => isShowing;

    private void Awake()
    {
        isQuitting = false;
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        BuildCanvas();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
    }

    private void OnDestroy()
    {
        if (isFrozenByPopup)
        {
            Time.timeScale = 1f;
            isFrozenByPopup = false;
        }
        if (canvasGo != null) Destroy(canvasGo);
        if (instance == this) instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HideImmediate();
    }

    private void BuildCanvas()
    {
        if (canvasGo != null) return;

        canvasGo = new GameObject("TutorialPopup_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var rootGo = new GameObject("PopupRoot", typeof(RectTransform), typeof(CanvasGroup));
        rootGo.transform.SetParent(canvasGo.transform, false);
        var rootRt = (RectTransform)rootGo.transform;
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        rootGroup = rootGo.GetComponent<CanvasGroup>();
        rootGroup.alpha = 0f;
        rootGroup.blocksRaycasts = false;
        rootGroup.interactable = false;

        // Semi-transparent dark backdrop while game is frozen
        var bgGo = new GameObject("BackdropDimmer", typeof(RectTransform));
        bgGo.transform.SetParent(rootRt, false);
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        backdropImage = bgGo.AddComponent<Image>();
        backdropImage.color = new Color(0f, 0f, 0f, 0.58f);
        backdropImage.raycastTarget = false;

        // Tutorial Book Image
        var imgGo = new GameObject("TutorialBookImage", typeof(RectTransform));
        imgGo.transform.SetParent(rootRt, false);
        imageRect = (RectTransform)imgGo.transform;
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.anchoredPosition = targetPosition;
        imageRect.sizeDelta = targetSize;

        tutorialImage = imgGo.AddComponent<Image>();
        tutorialImage.raycastTarget = false;
        tutorialImage.preserveAspect = true;

        // "PRESS SPACE TO CONTINUE" Prompt Text below the book
        var txtGo = new GameObject("PressSpacePrompt", typeof(RectTransform));
        txtGo.transform.SetParent(rootRt, false);
        promptRect = (RectTransform)txtGo.transform;
        promptRect.anchorMin = new Vector2(0.5f, 0.5f);
        promptRect.anchorMax = new Vector2(0.5f, 0.5f);
        promptRect.pivot = new Vector2(0.5f, 0.5f);
        promptRect.sizeDelta = new Vector2(1200f, 80f);
        promptRect.anchoredPosition = new Vector2(0f, -345f);

        promptText = txtGo.AddComponent<Text>();
        promptText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        promptText.fontSize = 44;
        promptText.fontStyle = FontStyle.Bold;
        promptText.alignment = TextAnchor.MiddleCenter;
        promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
        promptText.verticalOverflow = VerticalWrapMode.Overflow;
        promptText.raycastTarget = false;
        promptText.text = "PRESS SPACE TO CONTINUE";
        promptText.color = new Color(1f, 0.95f, 0.55f, 1f);

        var outline = txtGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);
    }

    public void Show(
        TutorialTrigger2D source,
        Sprite sprite,
        Vector2 size,
        Vector2 screenPos,
        bool freezeGame = true,
        string continueText = "PRESS SPACE TO CONTINUE")
    {
        if (sprite == null) return;
        if (canvasGo == null) BuildCanvas();

        activeTrigger = source;
        isShowing = true;
        hideTimer = -1f;
        unfreezeNextFrame = false;
        openedUnscaledTime = Time.unscaledTime;

        targetSize = size;
        targetPosition = screenPos;

        tutorialImage.sprite = sprite;
        imageRect.sizeDelta = targetSize;
        imageRect.anchoredPosition = targetPosition;

        if (backdropImage != null)
            backdropImage.enabled = freezeGame;

        if (promptText != null)
        {
            promptText.enabled = freezeGame;
            promptText.text = string.IsNullOrWhiteSpace(continueText) ? "PRESS SPACE TO CONTINUE" : continueText;
            promptRect.anchoredPosition = new Vector2(targetPosition.x, targetPosition.y - (targetSize.y * 0.5f) - 48f);
        }

        if (freezeGame)
        {
            Time.timeScale = 0f;
            isFrozenByPopup = true;
        }

        popTimer = popDuration;
        SoundManager.PlayButtonClick();
    }

    public void RequestHide(TutorialTrigger2D source, float delay)
    {
        if (activeTrigger != source) return;
        if (isFrozenByPopup) return; // When frozen, only pressing SPACE dismisses the popup

        if (delay <= 0.01f)
        {
            isShowing = false;
            hideTimer = -1f;
        }
        else
        {
            hideTimer = delay;
        }
    }

    public void DismissByPlayer()
    {
        if (!isShowing) return;

        isShowing = false;
        hideTimer = -1f;
        SoundManager.PlayButtonClick();

        if (isFrozenByPopup)
        {
            // Restore Time.timeScale on the next frame so the SPACE press that closed this popup
            // is not also read as a Jump input by PlayerController2D on the same frame.
            unfreezeNextFrame = true;
        }

        if (activeTrigger != null)
        {
            activeTrigger.NotifyDismissed();
        }
    }

    public void HideImmediate()
    {
        activeTrigger = null;
        isShowing = false;
        hideTimer = -1f;
        unfreezeNextFrame = false;

        if (isFrozenByPopup)
        {
            Time.timeScale = 1f;
            isFrozenByPopup = false;
        }

        if (rootGroup != null) rootGroup.alpha = 0f;
    }

    private static bool WasSpacePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && k.spaceKey.wasPressedThisFrame) return true;
        return false;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    private void Update()
    {
        if (unfreezeNextFrame)
        {
            unfreezeNextFrame = false;
            if (isFrozenByPopup)
            {
                Time.timeScale = 1f;
                isFrozenByPopup = false;
            }
        }

        if (rootGroup == null || imageRect == null) return;

        float dt = Time.unscaledDeltaTime;

        // Check for SPACE press while showing & frozen
        if (isShowing && isFrozenByPopup)
        {
            if (Time.unscaledTime - openedUnscaledTime >= minHoldBeforeDismiss && WasSpacePressedThisFrame())
            {
                DismissByPlayer();
            }
        }

        if (hideTimer > 0f && !isFrozenByPopup)
        {
            hideTimer -= dt;
            if (hideTimer <= 0f)
            {
                hideTimer = -1f;
                isShowing = false;
            }
        }

        float targetAlpha = isShowing ? 1f : 0f;
        float speed = isShowing ? fadeInSpeed : fadeOutSpeed;
        rootGroup.alpha = Mathf.MoveTowards(rootGroup.alpha, targetAlpha, speed * dt);

        float scale = 1f;
        if (popTimer > 0f)
        {
            popTimer = Mathf.Max(0f, popTimer - dt);
            float t = 1f - (popTimer / Mathf.Max(0.01f, popDuration));
            scale = Mathf.Lerp(0.85f, 1f, t) + Mathf.Sin(t * Mathf.PI) * 0.08f;
        }
        else if (!isShowing)
        {
            scale = Mathf.Lerp(0.92f, 1f, rootGroup.alpha);
        }

        imageRect.localScale = Vector3.one * scale;

        // Pulse the "PRESS SPACE TO CONTINUE" text
        if (promptText != null && promptText.enabled && rootGroup.alpha > 0.01f)
        {
            float pulse = 0.65f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.5f));
            Color c = promptText.color;
            c.a = pulse;
            promptText.color = c;

            float textScale = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 5.5f);
            promptRect.localScale = Vector3.one * textScale;
        }
    }
}
