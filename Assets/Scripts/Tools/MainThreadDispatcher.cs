using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Queues actions from background threads and executes them on Unity's main thread
/// during Update(). Required because UGS SDK events may fire off the main thread.
///
/// Add this component to the same DontDestroyOnLoad GameObject as LobbyManager.
/// </summary>
public class MainThreadDispatcher : MonoBehaviour
{
    // Static queue — survives scene loads and is shared across all instances
    private static readonly Queue<Action> _queue = new Queue<Action>();
    private static MainThreadDispatcher _instance;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // Destroy only this component, NOT the whole GameObject
            // (the GameObject may host LobbyManager or other critical components)
            Destroy(this);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    /// <summary>
    /// Enqueue an action to be executed on the main thread on the next Update() tick.
    /// Safe to call from any thread.
    /// </summary>
    public static void Enqueue(Action action)
    {
        if (action == null) return;
        lock (_queue) { _queue.Enqueue(action); }
    }

    private void Update()
    {
        if (_queue.Count == 0) return;

        // Swap pattern: copy under lock, clear, then invoke outside the lock
        Action[] pending;
        lock (_queue)
        {
            if (_queue.Count == 0) return;
            pending = _queue.ToArray();
            _queue.Clear();
        }

        foreach (Action action in pending)
        {
            try { action?.Invoke(); }
            catch (Exception e) { Debug.LogError($"[MainThreadDispatcher] Action threw: {e.Message}"); }
        }
    }
}
