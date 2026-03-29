#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools/Netcode/Fix Duplicate Scene Hashes
///
/// Run this ONCE after duplicating a scene that contains scene-placed NetworkObjects.
///
/// Root cause: duplicating a .unity file copies the serialized GlobalObjectIdHash on
/// every NetworkObject component verbatim. NGO uses these hashes to identify objects
/// across the network — duplicate hashes across two scenes cause NullReferenceExceptions
/// in NetworkMetrics and break RPCs in the duplicated scene.
///
/// Fix: for every NetworkObject in the active scene, destroy the component and
/// immediately re-add it. Unity recomputes GlobalObjectIdHash from the current
/// scene's GUID + object local file ID, producing a unique value. The scene is
/// then marked dirty so the new hashes are saved into the .unity file permanently.
///
/// Workflow:
///   1. Open the duplicated scene (e.g. CarromNew) in the Editor.
///   2. Click Tools → Netcode → Fix Duplicate Scene Hashes.
///   3. Confirm the dialog.
///   4. Save the scene (Ctrl+S).
///   Done — the scene now has unique hashes and will work correctly with NGO.
/// </summary>
public static class FixDuplicateSceneHashes
{
    [MenuItem("Tools/Netcode/Fix Duplicate Scene Hashes")]
    public static void FixHashes()
    {
        Scene activeScene = SceneManager.GetActiveScene();

        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            EditorUtility.DisplayDialog(
                "Fix Duplicate Scene Hashes",
                "No active scene is loaded. Open the duplicated scene first.",
                "OK");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Fix Duplicate Scene Hashes",
            $"This will regenerate GlobalObjectIdHash on ALL NetworkObject components in:\n\n" +
            $"  {activeScene.name}\n\n" +
            $"Run this ONLY on a duplicated scene, not on the original.\n\n" +
            $"The scene will be marked dirty — save it afterwards.\n\nProceed?",
            "Fix Hashes",
            "Cancel");

        if (!confirmed) return;

        // Collect all NetworkObjects in the active scene (including inactive GameObjects)
        var allObjects = new List<GameObject>();
        foreach (GameObject root in activeScene.GetRootGameObjects())
            CollectAll(root, allObjects);

        int fixedCount = 0;

        foreach (GameObject go in allObjects)
        {
            NetworkObject netObj = go.GetComponent<NetworkObject>();
            if (netObj == null) continue;

            // Record the Undo operation so the developer can revert if needed
            Undo.RegisterFullObjectHierarchyUndo(go, "Fix NetworkObject Hash");

            // Destroy the component — this removes the stale serialized hash
            Undo.DestroyObjectImmediate(netObj);

            // Re-add a fresh component — Unity computes a new GlobalObjectIdHash
            // from this scene's GUID + the object's local file ID
            Undo.AddComponent<NetworkObject>(go);

            fixedCount++;
        }

        if (fixedCount == 0)
        {
            EditorUtility.DisplayDialog(
                "Fix Duplicate Scene Hashes",
                "No NetworkObject components found in the active scene.",
                "OK");
            return;
        }

        // Mark the scene dirty so Unity knows it needs saving
        EditorSceneManager.MarkSceneDirty(activeScene);

        EditorUtility.DisplayDialog(
            "Fix Duplicate Scene Hashes",
            $"Done. Regenerated hashes on {fixedCount} NetworkObject(s) in '{activeScene.name}'.\n\n" +
            $"Save the scene now (Ctrl+S) to persist the new hashes.",
            "OK");

        Debug.Log($"[FixDuplicateSceneHashes] Regenerated {fixedCount} NetworkObject hash(es) in '{activeScene.name}'. Save the scene to persist.");
    }

    private static void CollectAll(GameObject go, List<GameObject> result)
    {
        result.Add(go);
        foreach (Transform child in go.transform)
            CollectAll(child.gameObject, result);
    }
}
#endif
