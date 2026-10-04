using UnityEngine;

/// <summary>
/// Builds a greybox test room from primitive BoxCollider2D rectangles when the scene starts.
/// Layout (left to right, 1 unit = 16 px, ground top at y = 0, relative to this object):
///   1. Flat ground for running
///   2. Two tall parallel walls (a shaft) for wall climbing / sliding / wall-jumping
///   3. A floating ceiling slab with a narrow vertical slot (corner-correction test while jumping)
///   4. A low tunnel block (corner-correction test while dashing)
///   5. A bottomless pit with a kill-zone Hazard2D under the whole level (coyote time / fast respawn)
///   6. A landing platform
/// Generation happens at runtime (Awake); in Play mode you can also use the context menu "Regenerate Level".
/// </summary>
[DefaultExecutionOrder(-100)]
public class GreyboxLevelGenerator : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private bool generateOnAwake = true;
    [Tooltip("Optional. The player is placed at the spawn point and told to respawn there.")]
    [SerializeField] private PlayerRespawn player;
    [SerializeField] private float pixelsPerUnit = 16f;

    [Header("Ground & run")]
    [SerializeField] private float groundThickness = 2f;
    [SerializeField] private float runLength = 24f;

    [Header("Wall shaft")]
    [SerializeField] private float wallHeight = 14f;
    [SerializeField] private float wallThickness = 1f;
    [SerializeField] private float wallGap = 4f;

    [Header("Ceiling slot (corner correction)")]
    [SerializeField] private float ceilingHeight = 4f;
    [SerializeField] private float ceilingThickness = 1.5f;
    [SerializeField] private float ceilingLength = 10f;
    [Tooltip("Slot width in pixels. The player hitbox is 14 px wide, so 18 leaves 2 px per side.")]
    [SerializeField] private float ceilingSlotPixels = 18f;

    [Header("Tunnel (dash corner correction)")]
    [SerializeField] private float tunnelLength = 6f;
    [Tooltip("Clear height in pixels. The player hitbox is 22 px tall.")]
    [SerializeField] private float tunnelHeightPixels = 24f;

    [Header("Pit")]
    [SerializeField] private float pitWidth = 10f;
    [SerializeField] private float landingLength = 10f;
    [SerializeField] private float killZoneDepth = 8f;

    [Header("Colours")]
    [SerializeField] private Color groundColor = new Color(0.45f, 0.45f, 0.5f);
    [SerializeField] private Color wallColor = new Color(0.33f, 0.38f, 0.55f);
    [SerializeField] private Color obstacleColor = new Color(0.55f, 0.4f, 0.4f);

    /// <summary>The player start point (valid after generation).</summary>
    public Transform SpawnPoint { get; private set; }

    private Transform root;

    private void Awake()
    {
        if (generateOnAwake) Generate();
    }

    [ContextMenu("Regenerate Level")]
    public void Generate()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("GreyboxLevelGenerator builds the level at runtime. Enter Play mode (or use 'Regenerate Level' while playing).");
            return;
        }

        if (root != null)
        {
            root.gameObject.SetActive(false);
            Destroy(root.gameObject);
        }
        root = new GameObject("GreyboxLevel").transform;
        root.SetParent(transform, false);

        // ---- Horizontal layout (x cursor) ----
        float wallAX = runLength;
        float wallBX = wallAX + wallThickness + wallGap;
        float ceilStart = wallBX + wallThickness + 4f;
        float ceilEnd = ceilStart + ceilingLength;
        float tunnelStart = ceilEnd + 2f;
        float tunnelEnd = tunnelStart + tunnelLength;
        float groundEnd = tunnelEnd + 2f;
        float pitEnd = groundEnd + pitWidth;
        float levelEnd = pitEnd + landingLength;
        float boundaryHeight = wallHeight + 4f;

        // ---- Ground (top at y = 0) ----
        Box("Ground_Run", 0f, -groundThickness, groundEnd, 0f, groundColor);
        Box("Ground_Landing", pitEnd, -groundThickness, levelEnd, 0f, groundColor);

        // ---- Wall shaft ----
        Box("Wall_A", wallAX, 0f, wallAX + wallThickness, wallHeight, wallColor);
        Box("Wall_B", wallBX, 0f, wallBX + wallThickness, wallHeight, wallColor);

        // ---- Ceiling slab with a narrow slot ----
        float slotW = ceilingSlotPixels / pixelsPerUnit;
        float slotStart = ceilStart + (ceilingLength - slotW) * 0.5f;
        float slabTop = ceilingHeight + ceilingThickness;
        Box("Ceiling_Left", ceilStart, ceilingHeight, slotStart, slabTop, obstacleColor);
        Box("Ceiling_Right", slotStart + slotW, ceilingHeight, ceilEnd, slabTop, obstacleColor);

        // ---- Low tunnel block ----
        float tunnelClear = tunnelHeightPixels / pixelsPerUnit;
        Box("Tunnel_Block", tunnelStart, tunnelClear, tunnelEnd, tunnelClear + 2f, obstacleColor);

        // ---- Level boundaries (invisible walls would hide bugs, so they are visible) ----
        Box("Boundary_Left", -wallThickness, -groundThickness, 0f, boundaryHeight, wallColor);
        Box("Boundary_Right", levelEnd, -groundThickness, levelEnd + wallThickness, boundaryHeight, wallColor);

        // ---- Kill zone under the whole level (translucent red) ----
        float kzTop = -groundThickness - killZoneDepth + 2f;
        GameObject kz = Box("KillZone", -10f, kzTop - 2f, levelEnd + 10f, kzTop, new Color(1f, 0.15f, 0.15f, 0.3f), true);
        kz.AddComponent<Hazard2D>().hazardName = "Pit";

        // ---- Spawn point ----
        var spawn = new GameObject("PlayerSpawn").transform;
        spawn.SetParent(root, false);
        spawn.localPosition = new Vector3(2f, 0.9f, 0f);
        SpawnPoint = spawn;

        PlacePlayer();
    }

    private void PlacePlayer()
    {
        if (player == null) return;

        player.spawnPoint = SpawnPoint;
        Vector3 p = SpawnPoint.position;
        player.transform.position = p;
        var rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.position = p;
    }

    /// <summary>Creates a rectangle from (minX,minY) to (maxX,maxY) with a sprite and a BoxCollider2D.</summary>
    private GameObject Box(string objName, float minX, float minY, float maxX, float maxY, Color color, bool isTrigger = false)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
        go.transform.localScale = new Vector3(maxX - minX, maxY - minY, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GreyboxSprites.Square;
        sr.color = color;

        var bc = go.AddComponent<BoxCollider2D>(); // unit sprite + scale = exact rectangle size
        bc.size = Vector2.one;
        bc.isTrigger = isTrigger;
        return go;
    }
}
