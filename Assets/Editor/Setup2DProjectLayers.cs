#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class Setup2DProjectLayers
{
    private const string PrefKey = "Setup2DProjectLayers_Initialized_v1";

    static Setup2DProjectLayers()
    {
        EditorApplication.delayCall += () =>
        {
            if (!SessionState.GetBool(PrefKey, false))
            {
                SessionState.SetBool(PrefKey, true);
                ApplyAll(silent: true);
            }
        };
    }

    [MenuItem("Tools/2D/Setup 2D Layers and Physics Matrix")]
    public static void MenuApplyAll()
    {
        ApplyAll(silent: false);
    }

    public static void ApplyAll(bool silent = false)
    {
        bool changedLayers = EnsureLayers();
        bool changedSorting = EnsureSortingLayers();
        bool changedPhysics = ConfigurePhysics2DMatrix();

        if (changedLayers || changedSorting || changedPhysics)
        {
            AssetDatabase.SaveAssets();
            if (!silent)
            {
                EditorUtility.DisplayDialog(
                    "Setup 2D Layers",
                    "2D Layers, Sorting Layers, and Physics2D Collision Matrix configured successfully!",
                    "OK"
                );
            }
            Debug.Log("[Setup2DProjectLayers] 2D Layers, Sorting Layers, and Physics2D Collision Matrix applied successfully.");
        }
        else if (!silent)
        {
            EditorUtility.DisplayDialog(
                "Setup 2D Layers",
                "All 2D Layers and Physics2D Collision settings are already up to date.",
                "OK"
            );
        }
    }

    private static bool EnsureLayers()
    {
        var tagManager = LoadTagManager();
        if (tagManager == null) return false;

        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null || !layers.isArray) return false;

        var targetLayers = new Dictionary<int, string>
        {
            { 8, "Ground" },
            { 9, "Player" },
            { 10, "PlayerProjectile" },
            { 11, "Enemy" },
            { 12, "EnemyProjectile" },
            { 13, "Interactable" },
            { 14, "OneWayPlatform" },
            { 15, "TriggerZone" }
        };

        bool modified = false;
        foreach (var kvp in targetLayers)
        {
            if (kvp.Key < layers.arraySize)
            {
                SerializedProperty p = layers.GetArrayElementAtIndex(kvp.Key);
                if (string.IsNullOrEmpty(p.stringValue))
                {
                    // Ensure layer name isn't already assigned to another index
                    bool exists = false;
                    for (int i = 0; i < layers.arraySize; i++)
                    {
                        if (layers.GetArrayElementAtIndex(i).stringValue == kvp.Value)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        p.stringValue = kvp.Value;
                        modified = true;
                    }
                }
            }
        }

        if (modified)
        {
            tagManager.ApplyModifiedProperties();
        }
        return modified;
    }

    private static bool EnsureSortingLayers()
    {
        var tagManager = LoadTagManager();
        if (tagManager == null) return false;

        SerializedProperty sortingLayers = tagManager.FindProperty("m_SortingLayers");
        if (sortingLayers == null || !sortingLayers.isArray) return false;

        string[] desiredLayers = new[]
        {
            "Default",
            "Background",
            "Midground",
            "Ground",
            "Interactables",
            "Entities",
            "Foreground",
            "VFX",
            "UI"
        };

        var existingNames = new HashSet<string>();
        int nextId = 1001;

        for (int i = 0; i < sortingLayers.arraySize; i++)
        {
            SerializedProperty elem = sortingLayers.GetArrayElementAtIndex(i);
            existingNames.Add(elem.FindPropertyRelative("name").stringValue);
            int id = elem.FindPropertyRelative("uniqueID").intValue;
            if (id >= nextId) nextId = id + 1;
        }

        bool modified = false;
        foreach (string layerName in desiredLayers)
        {
            if (!existingNames.Contains(layerName))
            {
                sortingLayers.InsertArrayElementAtIndex(sortingLayers.arraySize);
                SerializedProperty newElem = sortingLayers.GetArrayElementAtIndex(sortingLayers.arraySize - 1);
                newElem.FindPropertyRelative("name").stringValue = layerName;
                newElem.FindPropertyRelative("uniqueID").intValue = nextId++;
                newElem.FindPropertyRelative("locked").intValue = 0;
                existingNames.Add(layerName);
                modified = true;
            }
        }

        if (modified)
        {
            tagManager.ApplyModifiedProperties();
        }
        return modified;
    }

    private static bool ConfigurePhysics2DMatrix()
    {
        int player = LayerMask.NameToLayer("Player");
        int playerProj = LayerMask.NameToLayer("PlayerProjectile");
        int enemy = LayerMask.NameToLayer("Enemy");
        int enemyProj = LayerMask.NameToLayer("EnemyProjectile");

        if (player >= 0 && playerProj >= 0)
            Physics2D.IgnoreLayerCollision(player, playerProj, true);

        if (playerProj >= 0)
            Physics2D.IgnoreLayerCollision(playerProj, playerProj, true);

        if (enemy >= 0 && enemyProj >= 0)
            Physics2D.IgnoreLayerCollision(enemy, enemyProj, true);

        if (enemyProj >= 0)
            Physics2D.IgnoreLayerCollision(enemyProj, enemyProj, true);

        if (playerProj >= 0 && enemyProj >= 0)
            Physics2D.IgnoreLayerCollision(playerProj, enemyProj, true);

        return true;
    }

    private static SerializedObject LoadTagManager()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        return (assets != null && assets.Length > 0) ? new SerializedObject(assets[0]) : null;
    }
}
#endif
