using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages all Panel instances across scenes.
///
/// When DontDestroyOnLoad is used, PanelManager persists across scene loads.
/// On each scene load it performs a MERGE-SCAN: newly discovered panels are
/// added to the registry, but panels from persistent scenes (DontDestroyOnLoad)
/// are never removed. Stale references (destroyed panels from unloaded scenes)
/// are pruned before each lookup so Open() never calls into a dead object.
/// </summary>
public class PanelManager : MonoBehaviour
{
    private readonly Dictionary<string, Panel> panels = new Dictionary<string, Panel>();
    private static PanelManager singleton = null;

    public static PanelManager Singleton
    {
        get
        {
            if (singleton == null)
            {
                singleton = FindFirstObjectByType<PanelManager>();
                if (singleton == null)
                    singleton = new GameObject("PanelManager").AddComponent<PanelManager>();

                singleton.ScanAndMerge();
            }
            return singleton;
        }
    }

    private void Awake()
    {
        if (singleton != null && singleton != this)
        {
            Destroy(this);
            return;
        }
        singleton = this;
        DontDestroyOnLoad(gameObject);

        // Subscribe to scene loads so we pick up panels in every new scene
        SceneManager.sceneLoaded += OnSceneLoaded;
        ScanAndMerge();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (singleton == this) singleton = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[PanelManager] Scene loaded: '{scene.name}' — running merge-scan.");
        ScanAndMerge();
    }

    /// <summary>
    /// Scans all active Canvases and registers any Panel not yet in the dictionary.
    /// Existing entries are kept (covers DontDestroyOnLoad panels).
    /// Stale entries (destroyed objects) are pruned first.
    /// </summary>
    private void ScanAndMerge()
    {
        // Prune stale references from unloaded scenes
        var toRemove = new List<string>();
        foreach (var kv in panels)
        {
            if (kv.Value == null || kv.Value.gameObject == null)
                toRemove.Add(kv.Key);
        }
        foreach (var key in toRemove)
        {
            Debug.Log($"[PanelManager] Pruned stale panel: '{key}'");
            panels.Remove(key);
        }

        // Scan all Canvases (including inactive) across all loaded scenes
        Canvas[] allCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int added = 0;
        foreach (Canvas c in allCanvases)
        {
            Panel[] list = c.gameObject.GetComponentsInChildren<Panel>(true);
            foreach (Panel p in list)
            {
                if (string.IsNullOrEmpty(p.ID)) continue;
                if (panels.ContainsKey(p.ID)) continue; // already registered — keep existing

                p.Initialize();
                p.Canvas = c;
                panels.Add(p.ID, p);
                Debug.Log($"[PanelManager] Registered panel: '{p.ID}' from canvas '{c.name}'");
                added++;
            }
        }

        Debug.Log($"[PanelManager] ScanAndMerge complete — {added} new panel(s), {panels.Count} total: [{string.Join(", ", panels.Keys)}]");
    }

    public static Panel GetSingleton(string id)
    {
        if (Singleton.panels.TryGetValue(id, out Panel panel) && panel != null)
            return panel;

        // Panel may have been destroyed (scene unload) — re-scan and retry once
        Debug.Log($"[PanelManager] GetSingleton('{id}') — not found or stale, re-scanning.");
        Singleton.ScanAndMerge();
        return Singleton.panels.TryGetValue(id, out panel) && panel != null ? panel : null;
    }

    public static void Open(string id)
    {
        var panel = GetSingleton(id);
        if (panel != null)
        {
            Debug.Log($"[PanelManager] Opening panel: '{id}'");
            panel.transform.SetAsLastSibling();
            panel.Open();
        }
        else
        {
            Debug.LogWarning($"[PanelManager] Open failed — panel '{id}' not found.");
        }
    }

    public static void Close(string id)
    {
        var panel = GetSingleton(id);
        panel?.Close();
    }

    public static bool IsOpen(string id)
    {
        var panel = GetSingleton(id);
        return panel != null && panel.IsOpen;
    }

    public static void CloseAll()
    {
        foreach (var kv in Singleton.panels)
            kv.Value?.Close();
    }
}
