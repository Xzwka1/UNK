using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Empty Trigger Box for teleporting the player to the next map.
/// Requires the player to have collected <see cref="requiredBoxes"/> (default 3) QuestBoxes in the current map.
/// If the player touches this trigger with fewer than 3 boxes, teleportation is blocked and the top-left HUD shakes.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class TeleportTrigger2D : MonoBehaviour
{
    [Header("Quest Box Requirement")]
    [Tooltip("Number of QuestBoxes the player must collect before this teleport trigger activates.")]
    [SerializeField, Min(1)] private int requiredBoxes = 3;

    [Header("Next Map Destination")]
    [Tooltip("Optional: exact scene name to load. If left empty, automatically loads the next scene in Build Settings (buildIndex + 1).")]
    [SerializeField] private string targetSceneName = "";
    [Tooltip("Automatically load SceneManager.GetActiveScene().buildIndex + 1 when targetSceneName is empty.")]
    [SerializeField] private bool loadNextSceneInBuild = true;

    [Header("Events")]
    public UnityEvent OnTeleportSuccess;
    public UnityEvent OnTeleportDenied;

    private bool hasTeleported;
    private float lastDeniedTime = -10f;

    private void Reset()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            box.isTrigger = true;
            box.size = new Vector2(1.5f, 2.2f);
        }
    }

    private void OnValidate()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box != null) box.isTrigger = true;
    }

    private void Awake()
    {
        var cols = GetComponents<Collider2D>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null) cols[i].isTrigger = true;
        }

        if (QuestBoxManager.Instance != null)
        {
            QuestBoxManager.Instance.RequiredBoxes = requiredBoxes;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasTeleported) return;

        PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
        if (player == null) return;

        if (player.TryGetComponent(out PlayerRespawn respawn) && respawn.IsDead)
            return;

        int collected = QuestBoxManager.Instance != null ? QuestBoxManager.Instance.CollectedCount : 0;

        if (collected < requiredBoxes)
        {
            // Prevent duplicate trigger firing from Player root collider + Hurtbox child collider in the same frame
            if (Time.unscaledTime - lastDeniedTime < 0.5f) return;
            lastDeniedTime = Time.unscaledTime;

            Debug.Log($"[TeleportTrigger2D] Cannot teleport yet! Collected {collected}/{requiredBoxes} QuestBoxes.");
            QuestBoxManager.Instance?.NotifyInsufficientBoxes(requiredBoxes);
            OnTeleportDenied?.Invoke();
            return;
        }

        hasTeleported = true;
        Debug.Log($"[TeleportTrigger2D] All {collected}/{requiredBoxes} QuestBoxes collected! Teleporting to next map...");
        SoundManager.PlayItemPickup();
        OnTeleportSuccess?.Invoke();

        LoadNextMap();
    }

    private void LoadNextMap()
    {
        if (!string.IsNullOrWhiteSpace(targetSceneName))
        {
            SceneManager.LoadScene(targetSceneName.Trim());
            return;
        }

        if (loadNextSceneInBuild)
        {
            int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (nextIndex < SceneManager.sceneCountInBuildSettings)
            {
                SceneManager.LoadScene(nextIndex);
            }
            else
            {
                Debug.LogWarning($"[TeleportTrigger2D] Next scene (buildIndex {nextIndex}) is not in Build Settings. Returning to MainMenu (index 0) or add more maps in Build Settings.");
            }
        }
    }

    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.55f, 0.25f);
        Gizmos.DrawCube(box.offset, box.size);
        Gizmos.color = new Color(0.2f, 1f, 0.55f, 0.95f);
        Gizmos.DrawWireCube(box.offset, box.size);
    }
}
