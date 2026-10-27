using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controls the End Screen scene:
/// Displays Resources/UI/ENDScreen, smoothly brightens (fades in from black),
/// stays visible for 5 seconds, and then returns to MainMenu.
/// </summary>
public class EndScreenUI : MonoBehaviour
{
    [Header("Artwork")]
    [Tooltip("Auto-loaded from Resources/UI/ENDScreen if left empty.")]
    [SerializeField] private Sprite endScreenSprite;

    [Header("Timing")]
    [Tooltip("Seconds to gradually brighten the End Screen from black to full brightness.")]
    [SerializeField, Min(0.1f)] private float fadeInDuration = 2.5f;
    [Tooltip("Seconds to stay visible after brightening before returning to MainMenu.")]
    [SerializeField, Min(0f)] private float holdDurationAfterBright = 5f;
    [Tooltip("Seconds to fade out to black right before switching to MainMenu.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.6f;

    [Header("Destination")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private Image endImage;

    private void Start()
    {
        Time.timeScale = 1f;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.StopBGM();
            SoundManager.PlayStageClear();
        }

        BuildUI();
        StartCoroutine(PlayEndingSequence());
    }

    private void BuildUI()
    {
        if (endScreenSprite == null)
        {
            var all = Resources.LoadAll<Sprite>("UI/ENDScreen");
            if (all != null && all.Length > 0) endScreenSprite = all[0];
            else endScreenSprite = Resources.Load<Sprite>("UI/ENDScreen");
        }

        var canvasGo = new GameObject("EndScreenCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // 1. Solid Black Fullscreen Backdrop
        var bgGo = new GameObject("BlackBackdrop", typeof(RectTransform));
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = Color.black;
        bgImg.raycastTarget = false;

        // 2. End Screen Artwork (Centered, aspect-preserved)
        var artGo = new GameObject("EndScreenArtwork", typeof(RectTransform));
        artGo.transform.SetParent(canvasGo.transform, false);
        var artRect = artGo.GetComponent<RectTransform>();
        artRect.anchorMin = Vector2.zero;
        artRect.anchorMax = Vector2.one;
        artRect.offsetMin = new Vector2(20f, 20f);
        artRect.offsetMax = new Vector2(-20f, -20f);

        endImage = artGo.AddComponent<Image>();
        endImage.sprite = endScreenSprite;
        endImage.preserveAspect = true;
        endImage.color = new Color(0f, 0f, 0f, 0f); // Start completely dark
        endImage.raycastTarget = false;
    }

    private IEnumerator PlayEndingSequence()
    {
        // Phase 1: Gradually brighten from dark/black to full brightness
        float elapsed = 0f;
        float dur = Mathf.Max(0.1f, fadeInDuration);
        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            // Smooth S-curve easing
            float smooth = t * t * (3f - 2f * t);
            if (endImage != null)
            {
                endImage.color = new Color(smooth, smooth, smooth, smooth);
            }
            yield return null;
        }

        if (endImage != null)
        {
            endImage.color = Color.white;
        }

        // Phase 2: Wait 5 seconds at full brightness
        float waitElapsed = 0f;
        while (waitElapsed < holdDurationAfterBright)
        {
            waitElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Phase 3: Brief smooth fade out to black before returning to MainMenu
        if (fadeOutDuration > 0.01f && endImage != null)
        {
            float outElapsed = 0f;
            while (outElapsed < fadeOutDuration)
            {
                outElapsed += Time.unscaledDeltaTime;
                float t = 1f - Mathf.Clamp01(outElapsed / fadeOutDuration);
                endImage.color = new Color(t, t, t, t);
                yield return null;
            }
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }
}
