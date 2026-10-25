using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Empty Trigger Box for displaying a Tutorial Book image on screen when the player walks into it.
/// Freezes the game and shows "PRESS SPACE TO CONTINUE" until the player presses SPACE.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class TutorialTrigger2D : MonoBehaviour
{
    public enum TutorialStep
    {
        MoveAndJump,
        DoubleJump,
        WallGrip,
        WallJump,
        Dash,
        Custom
    }

    [Header("Tutorial Image Selection")]
    [Tooltip("Select which control tutorial to show, or pick Custom to assign any Sprite manually.")]
    [SerializeField] private TutorialStep tutorialType = TutorialStep.MoveAndJump;
    [Tooltip("The tutorial image displayed on screen when the player enters this trigger box.")]
    [SerializeField] private Sprite tutorialSprite;

    [Header("Freeze & Continue Settings")]
    [Tooltip("If true (default), freezes the game (Time.timeScale = 0) when triggered until the player presses SPACE.")]
    [SerializeField] private bool freezeGameOnTrigger = true;
    [Tooltip("Text shown below the tutorial image while the game is frozen.")]
    [SerializeField] private string continuePromptText = "PRESS SPACE TO CONTINUE";
    [Tooltip("If true (default), this trigger box only pops up once so it won't freeze the player repeatedly.")]
    [SerializeField] private bool triggerOnlyOnce = true;

    [Header("On-Screen Display Layout (1920x1080 reference)")]
    [Tooltip("Size of the tutorial book image on screen in pixels.")]
    [SerializeField] private Vector2 displaySize = new Vector2(880f, 660f);
    [Tooltip("Screen position offset from center.")]
    [SerializeField] private Vector2 screenOffset = new Vector2(0f, 50f);
    [Tooltip("Only used when freezeGameOnTrigger is false: seconds to keep showing after exiting trigger.")]
    [SerializeField, Min(0f)] private float hideDelayAfterExit = 0.5f;

    [Header("Events")]
    public UnityEvent OnTutorialShown;
    public UnityEvent OnTutorialHidden;

    [SerializeField, HideInInspector] private TutorialStep lastValidatedType = (TutorialStep)(-1);

    private readonly HashSet<Collider2D> activePlayerColliders = new HashSet<Collider2D>();
    private bool hasTriggeredOnce;

    private void Reset()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            box.isTrigger = true;
            box.size = new Vector2(2.5f, 2.5f);
        }

        lastValidatedType = tutorialType;
        AutoAssignSpriteForType(tutorialType, true);
    }

    private void OnValidate()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box != null) box.isTrigger = true;

        if (tutorialType != lastValidatedType || tutorialSprite == null)
        {
            lastValidatedType = tutorialType;
            AutoAssignSpriteForType(tutorialType, tutorialType != TutorialStep.Custom);
        }
    }

    private void AutoAssignSpriteForType(TutorialStep step, bool overwriteExisting)
    {
#if UNITY_EDITOR
        if (step == TutorialStep.Custom) return;
        if (!overwriteExisting && tutorialSprite != null) return;

        string path = step switch
        {
            TutorialStep.MoveAndJump => "Assets/tutorial/tutorial_Wove-Jump01.png",
            TutorialStep.DoubleJump  => "Assets/tutorial/tutorial_DoubleJump.png",
            TutorialStep.WallGrip    => "Assets/tutorial/tutorial_Wall_Grip.png",
            TutorialStep.WallJump    => "Assets/tutorial/tutorial_Wall_Jump.png",
            TutorialStep.Dash        => "Assets/tutorial/tutorial_Dash.png",
            _                        => null
        };

        if (!string.IsNullOrEmpty(path))
        {
            Sprite loaded = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (loaded != null)
                tutorialSprite = loaded;
        }
#endif
    }

    private void Awake()
    {
        var cols = GetComponents<Collider2D>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null) cols[i].isTrigger = true;
        }

        if (tutorialSprite == null)
            AutoAssignSpriteForType(tutorialType, true);
    }

    private void OnDisable()
    {
        if (activePlayerColliders.Count > 0)
        {
            activePlayerColliders.Clear();
            if (!freezeGameOnTrigger && TutorialPopupManager.Instance != null)
                TutorialPopupManager.Instance.RequestHide(this, 0f);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (triggerOnlyOnce && hasTriggeredOnce) return;

        PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
        if (player == null) return;

        if (player.TryGetComponent(out PlayerRespawn respawn) && respawn.IsDead)
            return;

        bool wasEmpty = activePlayerColliders.Count == 0;
        activePlayerColliders.Add(other);

        if (wasEmpty)
        {
            hasTriggeredOnce = true;
            if (TutorialPopupManager.Instance != null && tutorialSprite != null)
            {
                TutorialPopupManager.Instance.Show(
                    this,
                    tutorialSprite,
                    displaySize,
                    screenOffset,
                    freezeGameOnTrigger,
                    continuePromptText);
            }
            OnTutorialShown?.Invoke();
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!activePlayerColliders.Remove(other)) return;

        if (activePlayerColliders.Count == 0 && !freezeGameOnTrigger)
        {
            if (TutorialPopupManager.Instance != null)
            {
                TutorialPopupManager.Instance.RequestHide(this, hideDelayAfterExit);
            }
            OnTutorialHidden?.Invoke();
        }
    }

    public void NotifyDismissed()
    {
        OnTutorialHidden?.Invoke();
    }

    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 0.78f, 1f, 0.22f);
        Gizmos.DrawCube(box.offset, box.size);
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.95f);
        Gizmos.DrawWireCube(box.offset, box.size);
    }
}
