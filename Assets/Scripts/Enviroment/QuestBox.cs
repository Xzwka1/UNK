using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Collectible QuestBox item. Place 3 of these in a Map; when the player walks into one,
/// it increments the top-left QuestBox counter (x1 -> x2 -> x3) and disappears.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class QuestBox : MonoBehaviour
{
    [Header("Visuals (Auto-loaded from Assets/Tileset/QuestBox.png)")]
    [SerializeField] private Sprite questBoxSprite;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Idle Floating Effect")]
    [Tooltip("Gentle up-and-down bobbing motion so the box stands out as a collectible item.")]
    [SerializeField] private bool enableFloating = true;
    [SerializeField] private float floatAmplitude = 0.08f;
    [SerializeField] private float floatFrequency = 2.5f;

    [Header("Events")]
    public UnityEvent OnCollected;

    private bool isCollected;
    private Vector3 startPosition;
    private float floatPhaseOffset;

    private void Reset()
    {
        AutoLoadSpriteInEditor();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sortingLayerName = "Interactables";
        }
        if (questBoxSprite != null && spriteRenderer.sprite == null)
            spriteRenderer.sprite = questBoxSprite;

        var box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            box.isTrigger = true;
            if (spriteRenderer != null && spriteRenderer.sprite != null)
                box.size = spriteRenderer.sprite.bounds.size;
        }
    }

    private void OnValidate()
    {
        AutoLoadSpriteInEditor();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (!Application.isPlaying && spriteRenderer != null)
        {
            if (spriteRenderer.sprite == null && questBoxSprite != null)
                spriteRenderer.sprite = questBoxSprite;
            if (spriteRenderer.sortingLayerName == "Default")
                spriteRenderer.sortingLayerName = "Interactables";
        }

        var box = GetComponent<BoxCollider2D>();
        if (box != null) box.isTrigger = true;
    }

    private void AutoLoadSpriteInEditor()
    {
#if UNITY_EDITOR
        if (questBoxSprite == null)
            questBoxSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Tileset/QuestBox.png");
#endif
    }

    private void Awake()
    {
        AutoLoadSpriteInEditor();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null && spriteRenderer.sprite == null && questBoxSprite != null)
            spriteRenderer.sprite = questBoxSprite;

        var cols = GetComponents<Collider2D>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null) cols[i].isTrigger = true;
        }

        startPosition = transform.localPosition;
        floatPhaseOffset = (transform.position.x * 0.7f) + (transform.position.y * 0.3f);

        // Ensure the manager & HUD exist in the scene
        _ = QuestBoxManager.Instance;
    }

    private void Update()
    {
        if (isCollected || !enableFloating) return;

        float offsetY = Mathf.Sin((Time.time * floatFrequency) + floatPhaseOffset) * floatAmplitude;
        transform.localPosition = new Vector3(startPosition.x, startPosition.y + offsetY, startPosition.z);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isCollected) return;

        PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
        if (player == null) return;

        if (player.TryGetComponent(out PlayerRespawn respawn) && respawn.IsDead)
            return;

        isCollected = true;

        Sprite icon = (spriteRenderer != null && spriteRenderer.sprite != null) ? spriteRenderer.sprite : questBoxSprite;
        QuestBoxManager.Collect(icon);

        OnCollected?.Invoke();
        gameObject.SetActive(false);
    }
}
