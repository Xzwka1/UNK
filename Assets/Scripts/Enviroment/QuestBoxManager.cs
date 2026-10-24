using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Manages QuestBox collection per map and renders the Top-Left QuestBox HUD ([Box Icon] x1, x2, x3).
/// Automatically resets the box count when entering a new scene and blocks teleportation until
/// <see cref="RequiredBoxes"/> (default 3) have been collected.
/// </summary>
public class QuestBoxManager : MonoBehaviour
{
    private static QuestBoxManager instance;
    private static bool isQuitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
        isQuitting = false;
    }

    public static QuestBoxManager Instance
    {
        get
        {
            if (isQuitting || !Application.isPlaying) return instance;
            if (instance == null)
            {
                instance = FindFirstObjectByType<QuestBoxManager>();
                if (instance == null)
                {
                    var go = new GameObject("QuestBoxManager");
                    instance = go.AddComponent<QuestBoxManager>();
                }
            }
            return instance;
        }
    }

    [Header("Quest Settings")]
    [Tooltip("Number of QuestBoxes required to unlock the teleport trigger to the next map.")]
    [SerializeField, Min(1)] private int requiredBoxes = 3;
    [Tooltip("If false (default), the top-left [Box] x1/x2/x3 counter appears as soon as the 1st box is collected.")]
    [SerializeField] private bool showWhenZero = false;

    [Header("Top-Left HUD Layout (1920x1080 reference)")]
    [SerializeField] private Vector2 topLeftMargin = new Vector2(36f, -32f);
    [SerializeField] private Vector2 iconSize = new Vector2(86f, 98f);
    [SerializeField] private int counterFontSize = 54;
    [SerializeField] private int sortingOrder = 65;

    [Header("Visual Feedback")]
    [SerializeField] private Sprite boxIconSprite;
    [SerializeField] private float popDuration = 0.35f;
    [SerializeField] private float popScale = 1.35f;
    [SerializeField] private float deniedShakeDuration = 1.2f;

    public int CollectedCount { get; private set; }
    public int RequiredBoxes
    {
        get => requiredBoxes;
        set => requiredBoxes = Mathf.Max(1, value);
    }

    public bool HasEnoughBoxes(int required) => CollectedCount >= required;
    public bool HasAllBoxes => CollectedCount >= requiredBoxes;

    public event Action<int, int> OnBoxCountChanged;

    private GameObject canvasGo;
    private RectTransform hudContainer;
    private CanvasGroup hudGroup;
    private Image boxImage;
    private Text countText;
    private Text warningText;
    private CanvasGroup warningGroup;

    private float popTimer;
    private float shakeTimer;
    private float warningTimer;

    private static readonly Color NormalCountColor = Color.white;
    private static readonly Color CompleteCountColor = new Color(0.45f, 1f, 0.55f, 1f);
    private static readonly Color DeniedCountColor = new Color(1f, 0.38f, 0.32f, 1f);

    private void Awake()
    {
        isQuitting = false;
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        EnsureBoxSpriteLoaded();
        BuildHUD();
        RefreshHUD(false);
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
        if (canvasGo != null) Destroy(canvasGo);
        if (instance == this) instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResetCount();
    }

    public void ResetCount()
    {
        CollectedCount = 0;
        popTimer = 0f;
        shakeTimer = 0f;
        warningTimer = 0f;
        RefreshHUD(false);
        OnBoxCountChanged?.Invoke(CollectedCount, requiredBoxes);
    }

    /// <summary>Called when the player walks into a QuestBox in the scene.</summary>
    public static void Collect(Sprite fallbackSprite = null)
    {
        if (Instance == null) return;
        Instance.CollectInternal(fallbackSprite);
    }

    private void CollectInternal(Sprite fallbackSprite)
    {
        if (boxIconSprite == null && fallbackSprite != null)
        {
            boxIconSprite = fallbackSprite;
            if (boxImage != null) boxImage.sprite = boxIconSprite;
        }

        CollectedCount++;
        popTimer = popDuration;
        shakeTimer = 0f;
        warningTimer = 0f;

        SoundManager.PlayItemPickup();
        RefreshHUD(true);
        OnBoxCountChanged?.Invoke(CollectedCount, requiredBoxes);

        Debug.Log($"[QuestBoxManager] Collected QuestBox: x{CollectedCount} ({CollectedCount}/{requiredBoxes})");
    }

    /// <summary>Called when the player touches the Teleport Trigger without enough boxes.</summary>
    public void NotifyInsufficientBoxes(int needed)
    {
        requiredBoxes = Mathf.Max(requiredBoxes, needed);
        shakeTimer = deniedShakeDuration;
        warningTimer = 1.8f;

        if (warningText != null)
        {
            warningText.text = $"ต้องเก็บกล่องให้ครบ {needed} อัน! ({CollectedCount}/{needed})";
        }

        SoundManager.PlayLockoutDenied(false);
        RefreshHUD(false);
    }

    private void EnsureBoxSpriteLoaded()
    {
        if (boxIconSprite != null) return;

        Sprite[] sprites = Resources.LoadAll<Sprite>("UI/QuestBox");
        if (sprites != null && sprites.Length > 0)
        {
            boxIconSprite = sprites[0];
            return;
        }

        boxIconSprite = Resources.Load<Sprite>("UI/QuestBox");
        if (boxIconSprite != null) return;

#if UNITY_EDITOR
        boxIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Tileset/QuestBox.png");
#endif
    }

    private void BuildHUD()
    {
        if (canvasGo != null) return;

        canvasGo = new GameObject("QuestBoxHUD_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Top-Left Container
        var containerGo = new GameObject("QuestBox_TopLeft", typeof(RectTransform), typeof(CanvasGroup));
        containerGo.transform.SetParent(canvasGo.transform, false);
        hudContainer = (RectTransform)containerGo.transform;
        hudContainer.anchorMin = new Vector2(0f, 1f);
        hudContainer.anchorMax = new Vector2(0f, 1f);
        hudContainer.pivot = new Vector2(0f, 1f);
        hudContainer.anchoredPosition = topLeftMargin;
        hudContainer.sizeDelta = new Vector2(360f, 110f);

        hudGroup = containerGo.GetComponent<CanvasGroup>();
        hudGroup.blocksRaycasts = false;
        hudGroup.interactable = false;

        // Box Icon (Left)
        var iconGo = new GameObject("BoxIcon", typeof(RectTransform));
        iconGo.transform.SetParent(hudContainer, false);
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.anchoredPosition = new Vector2(0f, 0f);
        iconRt.sizeDelta = iconSize;

        boxImage = iconGo.AddComponent<Image>();
        boxImage.raycastTarget = false;
        boxImage.preserveAspect = true;
        if (boxIconSprite != null)
            boxImage.sprite = boxIconSprite;

        // Counter Text ("x1", "x2", "x3") right next to the box icon
        var textGo = new GameObject("CountText", typeof(RectTransform));
        textGo.transform.SetParent(hudContainer, false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = new Vector2(0f, 0.5f);
        textRt.anchorMax = new Vector2(0f, 0.5f);
        textRt.pivot = new Vector2(0f, 0.5f);
        textRt.anchoredPosition = new Vector2(iconSize.x + 18f, -2f);
        textRt.sizeDelta = new Vector2(260f, 90f);

        countText = textGo.AddComponent<Text>();
        countText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        countText.fontSize = counterFontSize;
        countText.fontStyle = FontStyle.Bold;
        countText.alignment = TextAnchor.MiddleLeft;
        countText.horizontalOverflow = HorizontalWrapMode.Overflow;
        countText.verticalOverflow = VerticalWrapMode.Overflow;
        countText.raycastTarget = false;
        countText.color = NormalCountColor;

        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(3f, -3f);

        // Warning message below the top-left icon when trying to teleport without 3 boxes
        var warnGo = new GameObject("IncompleteWarning", typeof(RectTransform), typeof(CanvasGroup));
        warnGo.transform.SetParent(canvasGo.transform, false);
        var warnRt = (RectTransform)warnGo.transform;
        warnRt.anchorMin = new Vector2(0f, 1f);
        warnRt.anchorMax = new Vector2(0f, 1f);
        warnRt.pivot = new Vector2(0f, 1f);
        warnRt.anchoredPosition = new Vector2(topLeftMargin.x, topLeftMargin.y - 110f);
        warnRt.sizeDelta = new Vector2(700f, 60f);

        warningGroup = warnGo.GetComponent<CanvasGroup>();
        warningGroup.alpha = 0f;
        warningGroup.blocksRaycasts = false;
        warningGroup.interactable = false;

        warningText = warnGo.AddComponent<Text>();
        warningText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        warningText.fontSize = 32;
        warningText.fontStyle = FontStyle.Bold;
        warningText.alignment = TextAnchor.UpperLeft;
        warningText.horizontalOverflow = HorizontalWrapMode.Overflow;
        warningText.raycastTarget = false;
        warningText.color = DeniedCountColor;

        var warnOutline = warnGo.AddComponent<Outline>();
        warnOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        warnOutline.effectDistance = new Vector2(2.5f, -2.5f);
    }

    private void RefreshHUD(bool collectedJustNow)
    {
        if (hudContainer == null) BuildHUD();

        EnsureBoxSpriteLoaded();
        if (boxImage != null && boxImage.sprite == null && boxIconSprite != null)
            boxImage.sprite = boxIconSprite;

        if (countText != null)
        {
            countText.text = $"x{CollectedCount}";
            if (shakeTimer > 0f && CollectedCount < requiredBoxes)
                countText.color = DeniedCountColor;
            else if (CollectedCount >= requiredBoxes)
                countText.color = CompleteCountColor;
            else
                countText.color = NormalCountColor;
        }

        bool shouldShow = showWhenZero || CollectedCount > 0 || shakeTimer > 0f;
        if (hudGroup != null)
        {
            hudGroup.alpha = shouldShow ? 1f : 0f;
        }
    }

    private void Update()
    {
        if (hudContainer == null) return;

        float dt = Time.unscaledDeltaTime;
        Vector2 basePos = topLeftMargin;
        float scale = 1f;

        if (popTimer > 0f)
        {
            popTimer = Mathf.Max(0f, popTimer - dt);
            float k = popTimer / Mathf.Max(0.01f, popDuration);
            scale = Mathf.Lerp(1f, popScale, Mathf.Sin(k * Mathf.PI));
        }

        if (shakeTimer > 0f)
        {
            shakeTimer = Mathf.Max(0f, shakeTimer - dt);
            float k = shakeTimer / Mathf.Max(0.01f, deniedShakeDuration);
            basePos.x += Mathf.Sin(Time.unscaledTime * 55f) * 10f * k;
            if (shakeTimer <= 0f)
            {
                RefreshHUD(false);
            }
        }

        if (warningTimer > 0f)
        {
            warningTimer = Mathf.Max(0f, warningTimer - dt);
            if (warningGroup != null)
                warningGroup.alpha = Mathf.Clamp01(warningTimer / 0.35f);
        }
        else if (warningGroup != null && warningGroup.alpha > 0f)
        {
            warningGroup.alpha = 0f;
        }

        hudContainer.anchoredPosition = basePos;
        hudContainer.localScale = Vector3.one * scale;
    }
}
