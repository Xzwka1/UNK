using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Delivery goal altar. Put on a trigger collider; when the player touches it with all 3 QuestBoxes,
/// fires <see cref="OnGoalReached"/> and loads the next scene in Build Settings (buildIndex + 1).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class GoalAltar : MonoBehaviour
{
    [Header("Next Map Teleport")]
    [Tooltip("Number of QuestBoxes required to activate the goal teleport.")]
    [SerializeField] private int requiredBoxes = 3;
    [Tooltip("Automatically load SceneManager.GetActiveScene().buildIndex + 1 when touched.")]
    [SerializeField] private bool loadNextScene = true;

    [Tooltip("Fired once when the player reaches the altar.")]
    public UnityEvent OnGoalReached;

    public bool Reached { get; private set; }
    private float lastDeniedTime = -10f;

    private void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Reached) return;
        if (other.GetComponentInParent<PlayerController2D>() == null) return;

        int collected = QuestBoxManager.Instance != null ? QuestBoxManager.Instance.CollectedCount : 0;
        if (requiredBoxes > 0 && collected < requiredBoxes)
        {
            if (Time.unscaledTime - lastDeniedTime < 0.5f) return;
            lastDeniedTime = Time.unscaledTime;
            QuestBoxManager.Instance?.NotifyInsufficientBoxes(requiredBoxes);
            return;
        }

        Reached = true;
        Debug.Log("[GoalAltar] Goal reached!");
        OnGoalReached?.Invoke();
        SoundManager.PlayStageClear();

        if (loadNextScene)
        {
            int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (nextIndex < SceneManager.sceneCountInBuildSettings)
            {
                SceneManager.LoadScene(nextIndex);
            }
            else
            {
                Debug.LogWarning($"[GoalAltar] Next scene (buildIndex {nextIndex}) is not in Build Settings. Add scenes under File > Build Profiles / Build Settings.");
            }
        }
    }
}
