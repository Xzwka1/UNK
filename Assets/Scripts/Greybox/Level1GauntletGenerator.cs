using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

/// <summary>
/// Procedural greybox builder for "Level 1: The Fragility Gauntlet".
///
/// HOW TO USE
///  1. Add this component to an empty GameObject (its position is the level origin).
///  2. Right-click the component header -> "Generate Level 1" (works in Edit mode; the blocks are saved in the scene),
///     or tick "Generate On Start" to rebuild at Play. "Clear Level" removes everything it made.
///  3. Tweak the multipliers and regenerate.
///
/// LAYOUT CONVENTION: every (x, y) in the layout is the BOTTOM-LEFT corner of a block, in world units
/// (so "Start platform (0,0) w8 h2" occupies x 0..8, y 0..2). X positions are scaled by <see cref="distanceMultiplier"/>,
/// Y positions by <see cref="heightMultiplier"/>; walls/shafts also scale their height, gap-spanning pits scale their width.
///
/// BLOCK TYPES (colour -> components)
///   Ground / wall   #3A3A3C  BoxCollider2D, layer "Ground"
///   Fragile         #FF9F0A  BoxCollider2D + FragilePlatform (existing script: 0.5 s crack, 3 s reform; no stress recovery)
///   Breaking barrier#32ADE6  BoxCollider2D + BreakingBarrier  (Dash breaks it, refunds 1 dash)
///   Spikes          #FF3B30  trigger BoxCollider2D + Hazard2D, tag "Hazard"
///   Goal altar      #30D158  trigger BoxCollider2D + GoalAltar, tag "Goal"
/// The "Hazard", "Goal" tags and the "Ground" layer are created automatically in the Editor if missing.
/// </summary>
[DisallowMultipleComponent]
public class Level1GauntletGenerator : MonoBehaviour
{
    public enum BlockKind { Ground, Fragile, Barrier, Spikes, Goal }

    [System.Serializable]
    private struct ZoneInfo
    {
        public string label;
        public Color color;
        public Bounds bounds;
    }

    // ------------------------------------------------------------------ Inspector
    [Header("Build")]
    [SerializeField] private bool generateOnStart = false;
    [Tooltip("Optional. Placed at the spawn point and told to respawn there. Auto-found if empty.")]
    [SerializeField] private PlayerRespawn player;
    [Tooltip("Add a big hazard strip far below the level so falling out of the map always respawns.")]
    [SerializeField] private bool addSafetyKillZone = true;

    [Header("Tuning multipliers (1 = design values)")]
    [Tooltip("Scales every X position (gaps between platforms grow/shrink; block widths stay the same).")]
    [SerializeField, Min(0.1f)] private float distanceMultiplier = 1f;
    [Tooltip("Scales every Y position and the height of walls / drop shafts.")]
    [SerializeField, Min(0.1f)] private float heightMultiplier = 1f;
    [Tooltip("Zone 1 precision-brake gap (units) between the start platform and the tiny platform.")]
    [SerializeField, Min(0.5f)] private float brakeGap = 4f;
    [Tooltip("Zone 3 Shatter Gap width in units (design value 14). Calibrate so a Stage 1 dash falls short.")]
    [SerializeField, Min(2f)] private float shatterGapWidth = 14f;
    [Tooltip("Extra multiplier on the Shatter Gap width.")]
    [SerializeField, Min(0.1f)] private float shatterGapMultiplier = 1f;

    [Header("Spawn")]
    [Tooltip("Offset from the Start platform's top-left corner (0, 2). Keeps the capsule fully on the platform.")]
    [SerializeField] private Vector2 spawnOffset = new Vector2(1.5f, 0.1f);

    [Header("Colours (hex)")]
    [SerializeField] private Color groundColor = Hex("#3A3A3C");
    [SerializeField] private Color fragileColor = Hex("#FF9F0A");
    [SerializeField] private Color barrierColor = Hex("#32ADE6");
    [SerializeField] private Color spikesColor = Hex("#FF3B30");
    [SerializeField] private Color goalColor = Hex("#30D158");

    [Header("Gizmos")]
    [SerializeField] private bool drawZoneGizmos = true;

    [SerializeField, HideInInspector] private List<ZoneInfo> zones = new List<ZoneInfo>();
    [SerializeField, HideInInspector] private Vector3 spawnLocal;

    private const string RootName = "Level1_Gauntlet";
    private const string HazardTag = "Hazard";
    private const string GoalTag = "Goal";
    private const string GroundLayerName = "Ground";

    private Transform root;
    private int zoneIndex = -1;
    private static bool warnedTag, warnedLayer;

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    private void Start()
    {
        if (generateOnStart) Generate();
    }

    // ------------------------------------------------------------------ Coordinate helpers
    private float X(float x) => x * distanceMultiplier;
    private float Y(float y) => y * heightMultiplier;

    // ------------------------------------------------------------------ Entry points
    [ContextMenu("Generate Level 1")]
    public void Generate()
    {
        Clear();
        EnsureTagsAndLayers();

        root = new GameObject(RootName).transform;
        root.SetParent(transform, false);
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(root.gameObject, "Generate Level 1");
#endif
        zones.Clear();
        zoneIndex = -1;

        // Zone 3 gap: everything at or beyond the runway shifts by (gap - design gap of 14).
        float gapShift = shatterGapWidth * shatterGapMultiplier - 14f;

        BuildZone1();
        BuildZone2();
        BuildZone3(gapShift);
        BuildZone4(gapShift);

        if (addSafetyKillZone)
        {
            BeginZone("Safety Kill Zone", new Color(1f, 0.2f, 0.2f, 0.4f));
            CreateHazard("KillZone_Bottom", -40f, Y(-30f), X(140f + gapShift) + 80f, 2f, false);
        }

        PlacePlayer();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    [ContextMenu("Clear Level")]
    public void Clear()
    {
        Transform existing = transform.Find(RootName);
        while (existing != null)
        {
            if (Application.isPlaying) { existing.gameObject.SetActive(false); Destroy(existing.gameObject); }
#if UNITY_EDITOR
            else Undo.DestroyObjectImmediate(existing.gameObject);
#endif
            existing = Application.isPlaying ? null : transform.Find(RootName);
        }
        zones.Clear();
    }

    // ------------------------------------------------------------------ ZONE 1: Baseline Cushion (Stage 1)
    private void BuildZone1()
    {
        BeginZone("ZONE 1 - Baseline Cushion (Stage 1)", new Color(0.3f, 0.8f, 1f, 0.9f));

        // Start platform (0,0) w8 h2; spawn on top at (0,2) (+ spawnOffset).
        CreateBlock("Start_Platform", BlockKind.Ground, 0f, 0f, 8f, 2f);
        spawnLocal = new Vector3(X(0f) + spawnOffset.x, Y(2f) + spawnOffset.y, 0f);

        // Spike bed under the whole precision section (y -4..-3), stops at the drop shaft wall.
        CreateHazard("Spikes_Zone1_Pit", 8f, -4f, 12f, 1f, true);

        // Precision Brake Test: gap -> tiny 2x2 platform (tests 0.02 s instant deceleration).
        float tinyX = 8f + brakeGap;
        CreateBlock("Brake_Tiny_Platform", BlockKind.Ground, tinyX, 0f, 2f, 2f);

        // Grip & Observe wall (15,0) w2 h8, spikes below (pit bed above).
        CreateBlock("Grip_Wall", BlockKind.Ground, 15f, 0f, 2f, 8f, false, true);

        // Landing platform (20,6): top at y=6.
        CreateBlock("Drop_Entry_Platform", BlockKind.Ground, 20f, 5f, 2f, 1f);

        // Cushion Drop: left shaft wall + deep floor (26,-14) w10 (extended left to x=22 so the whole shaft has a floor).
        CreateBlock("Drop_Shaft_LeftWall", BlockKind.Ground, 20f, -14f, 2f, 19f, false, true);
        CreateBlock("Cushion_Floor", BlockKind.Ground, 22f, -14f, 14f, 2f);
    }

    // ------------------------------------------------------------------ ZONE 2: Slippery Chimney (Stage 2)
    private void BuildZone2()
    {
        BeginZone("ZONE 2 - Slippery Chimney (Stage 2 + Active Recovery)", new Color(1f, 0.85f, 0.2f, 0.9f));

        // Forced climb wall (34,-14) w2 h16 -> top at y=2. Climb its left face with Ctrl+W.
        CreateBlock("Forced_Climb_Wall", BlockKind.Ground, 34f, -14f, 2f, 16f, false, true);

        // Chimney: left wall sits 2 units above the climb wall top so the player can crest onto it and step in.
        CreateBlock("Chimney_LeftWall", BlockKind.Ground, 34f, 4f, 2f, 10f, false, true);
        CreateBlock("Chimney_RightWall", BlockKind.Ground, 42f, 2f, 2f, 12f, false, true); // interior 36..42 = 6 wide
        CreateHazard("Spikes_Chimney_Floor", 36f, 1f, 6f, 1f, true);

        // Ceiling spike trap (blocks going higher); 2-unit opening to the right between wall tops and spikes.
        CreateHazard("Spikes_Chimney_Ceiling", 34f, 16f, 10f, 1f, true);

        // Active-recovery slide wall (46,16) down to (46,2): hold Ctrl+S all the way down.
        CreateBlock("Slide_Recovery_Wall", BlockKind.Ground, 46f, 2f, 2f, 14f, false, true);
    }

    // ------------------------------------------------------------------ ZONE 3: Shatter Gap (Stage 3)
    private void BuildZone3(float gapShift)
    {
        BeginZone("ZONE 3 - Shatter Gap (Stage 3 + Last Stand Dash)", new Color(1f, 0.4f, 0.2f, 0.9f));

        // Fragile launchpad (50,0) w3 h1 + short stress-pump wall on top of its right end.
        CreateFragile("Fragile_Launchpad", 50f, 0f, 3f, 1f);
        CreateBlock("Stress_Pump_Wall", BlockKind.Ground, 52f, 1f, 1f, 4f, false, true);

        // Spike bed under the slide-wall gap and the whole shatter gap (x 44 .. 66 + gapShift).
        CreateHazard("Spikes_Shatter_Gap", 44f, -4f, 22f + gapShift, 1f, true);

        // Landing: fragile runway (66,0) w8 h1 with the breaking barrier at its left edge (w1.5 h4).
        float runwayX = 66f + gapShift;
        CreateFragile("Fragile_Runway", runwayX, 0f, 8f, 1f);
        CreateBarrier("Last_Stand_Barrier", runwayX, 1f, 1.5f, 4f);
    }

    // ------------------------------------------------------------------ ZONE 4: Fragile Drop & Goal
    private void BuildZone4(float gapShift)
    {
        BeginZone("ZONE 4 - Fragile Drop & Goal (Terminal Velocity)", new Color(0.4f, 1f, 0.5f, 0.9f));

        float s = gapShift;

        // Slide-brake wall along the shaft's left side: (74,0) down to (74,-18).
        CreateBlock("Brake_Slide_Wall", BlockKind.Ground, 74f + s, -18f, 1f, 18f, false, true);

        // Final floor (78,-20) w14 (extended left to the wall so the whole shaft has a floor) + goal altar (86,-18).
        CreateBlock("Final_Floor", BlockKind.Ground, 75f + s, -20f, 17f, 2f);
        CreateGoal("Goal_Altar", 86f + s, -18f, 1.5f, 2f);
    }

    // ------------------------------------------------------------------ Block helpers
    /// <summary>Solid greybox rectangle on the Ground layer. (x,y) = bottom-left in layout units.</summary>
    private GameObject CreateBlock(string objName, BlockKind kind, float x, float y, float w, float h,
                                   bool scaleWidth = false, bool scaleHeight = false)
    {
        float width = scaleWidth ? w * distanceMultiplier : w;
        float height = scaleHeight ? h * heightMultiplier : h;

        var go = new GameObject(objName);
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(X(x) + width * 0.5f, Y(y) + height * 0.5f, 0f);
        go.transform.localScale = new Vector3(width, height, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSquareSprite();
        sr.color = ColorFor(kind);
        sr.sortingOrder = kind == BlockKind.Spikes || kind == BlockKind.Goal ? 1 : 0;

        var bc = go.AddComponent<BoxCollider2D>(); // unit sprite x scale = exact rectangle
        bc.size = Vector2.one;

        int ground = LayerMask.NameToLayer(GroundLayerName);
        if (ground >= 0) go.layer = ground;

        ExpandZone(new Bounds(go.transform.position, new Vector3(width, height, 0f)));
        return go;
    }

    /// <summary>Crumbling platform: yellow-orange, existing FragilePlatform component (no stress recovery while standing).</summary>
    private GameObject CreateFragile(string objName, float x, float y, float w, float h)
    {
        GameObject go = CreateBlock(objName, BlockKind.Fragile, x, y, w, h);
        go.AddComponent<FragilePlatform>(); // added AFTER the collider/sprite so its Awake finds them
        return go;
    }

    /// <summary>Dash-breakable wall: cyan, BreakingBarrier component (breaking it refunds 1 dash).</summary>
    private GameObject CreateBarrier(string objName, float x, float y, float w, float h)
    {
        GameObject go = CreateBlock(objName, BlockKind.Barrier, x, y, w, h);
        go.AddComponent<BreakingBarrier>();
        return go;
    }

    /// <summary>Deadly spikes: red trigger, Hazard2D (what the player Hurtbox looks for) and the "Hazard" tag.</summary>
    private GameObject CreateHazard(string objName, float x, float y, float w, float h, bool scaleWidth)
    {
        GameObject go = CreateBlock(objName, BlockKind.Spikes, x, y, w, h, scaleWidth, false);
        go.layer = 0; // hazards are triggers, not ground
        go.GetComponent<BoxCollider2D>().isTrigger = true;
        go.AddComponent<Hazard2D>().hazardName = objName;
        TrySetTag(go, HazardTag);
        return go;
    }

    /// <summary>Delivery goal: green trigger with GoalAltar and the "Goal" tag.</summary>
    private GameObject CreateGoal(string objName, float x, float y, float w, float h)
    {
        GameObject go = CreateBlock(objName, BlockKind.Goal, x, y, w, h);
        go.layer = 0;
        go.GetComponent<BoxCollider2D>().isTrigger = true;
        go.AddComponent<GoalAltar>();
        TrySetTag(go, GoalTag);
        return go;
    }

    private Color ColorFor(BlockKind kind)
    {
        switch (kind)
        {
            case BlockKind.Fragile: return fragileColor;
            case BlockKind.Barrier: return barrierColor;
            case BlockKind.Spikes: return spikesColor;
            case BlockKind.Goal: return goalColor;
            default: return groundColor;
        }
    }

    // ------------------------------------------------------------------ Zones / spawn
    private void BeginZone(string label, Color color)
    {
        zoneIndex = zones.Count;
        zones.Add(new ZoneInfo { label = label, color = color, bounds = new Bounds(transform.position, Vector3.zero), });
        zoneFirst = true;
    }

    private bool zoneFirst;

    private void ExpandZone(Bounds b)
    {
        if (zoneIndex < 0 || zoneIndex >= zones.Count) return;
        ZoneInfo z = zones[zoneIndex];
        if (zoneFirst) { z.bounds = b; zoneFirst = false; }
        else z.bounds.Encapsulate(b);
        zones[zoneIndex] = z;
    }

    private void PlacePlayer()
    {
        var spawn = new GameObject("PlayerSpawn").transform;
        spawn.SetParent(root, false);
        spawn.localPosition = spawnLocal;

        if (player == null) player = FindFirstObjectByType<PlayerRespawn>();
        if (player == null) return;

        player.spawnPoint = spawn;
        player.transform.position = spawn.position;
        var rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.position = spawn.position;
    }

    // ------------------------------------------------------------------ Sprite / tag / layer plumbing
    private static Sprite GetSquareSprite()
    {
#if UNITY_EDITOR
        // Edit-mode: use a real sprite ASSET so the scene keeps the reference after saving.
        if (!Application.isPlaying) return GetOrCreateEditorSprite();
#endif
        return GreyboxSprites.Square;
    }

    private static void TrySetTag(GameObject go, string tag)
    {
        try { go.tag = tag; }
        catch (UnityException)
        {
            if (!warnedTag)
            {
                warnedTag = true;
                Debug.LogWarning("[Level1Gauntlet] Tag '" + tag + "' is missing. Add it in Project Settings > Tags and Layers " +
                                 "(it is created automatically when generating in the Editor). Hazards still work through Hazard2D.");
            }
        }
    }

    private void EnsureTagsAndLayers()
    {
#if UNITY_EDITOR
        EnsureTag(HazardTag);
        EnsureTag(GoalTag);
        EnsureLayer(GroundLayerName);
#endif
        if (LayerMask.NameToLayer(GroundLayerName) < 0 && !warnedLayer)
        {
            warnedLayer = true;
            Debug.LogWarning("[Level1Gauntlet] Layer 'Ground' does not exist; solid blocks stay on Default. " +
                             "(No free user layer slot, or generating outside the Editor.)");
        }
    }

#if UNITY_EDITOR
    private static Sprite GetOrCreateEditorSprite()
    {
        const string path = "Assets/Greybox/GreyboxSquare.png";
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Greybox"));
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "Greybox/GreyboxSquare.png"), tex.EncodeToPNG());
        DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = 4f;          // 4 px / 4 ppu = exactly 1 world unit
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static SerializedObject LoadTagManager()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        return assets != null && assets.Length > 0 ? new SerializedObject(assets[0]) : null;
    }

    private static void EnsureTag(string tag)
    {
        foreach (string t in UnityEditorInternal.InternalEditorUtility.tags)
            if (t == tag) return;

        SerializedObject tm = LoadTagManager();
        if (tm == null) return;
        SerializedProperty tags = tm.FindProperty("tags");
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        tm.ApplyModifiedProperties();
    }

    private static void EnsureLayer(string layerName)
    {
        if (LayerMask.NameToLayer(layerName) >= 0) return;

        SerializedObject tm = LoadTagManager();
        if (tm == null) return;
        SerializedProperty layers = tm.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++) // 0-7 are built in
        {
            SerializedProperty p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue))
            {
                p.stringValue = layerName;
                tm.ApplyModifiedProperties();
                return;
            }
        }
    }

    // ------------------------------------------------------------------ Gizmos
    private void OnDrawGizmos()
    {
        if (!drawZoneGizmos) return;

        if (zones == null || zones.Count == 0)
        {
            Handles.Label(transform.position + Vector3.up, "Level1GauntletGenerator: right-click > Generate Level 1");
            return;
        }

        var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        foreach (ZoneInfo z in zones)
        {
            Gizmos.color = z.color;
            Gizmos.DrawWireCube(z.bounds.center, z.bounds.size + Vector3.one * 1.5f);
            style.normal.textColor = z.color;
            Handles.Label(new Vector3(z.bounds.min.x, z.bounds.max.y + 1.5f, 0f), z.label, style);
        }

        Gizmos.color = Color.white;
        Vector3 sp = transform.TransformPoint(spawnLocal);
        Gizmos.DrawWireSphere(sp, 0.4f);
        Handles.Label(sp + Vector3.up * 0.8f, "SPAWN");
    }
#endif
}
