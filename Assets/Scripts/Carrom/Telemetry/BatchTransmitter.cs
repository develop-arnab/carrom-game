using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Symmetrical two-way telemetry pipeline.
///
/// Host shoots  → records locally → TransmitFullReplay sends via ClientRpc  → Client's PlaybackEngine plays it.
/// Client shoots → records locally → TransmitFullReplay sends via ServerRpc  → Host's PlaybackEngine plays it.
///
/// The sender never routes data to themselves, so no "active player" filtering
/// is needed inside the receive RPCs — they always run on the spectator.
///
/// NT and NRB2D removed from all prefabs (Path A). Zero NetworkTransform references.
/// </summary>
public class BatchTransmitter : NetworkBehaviour
{
    private const int FRAMES_PER_CHUNK = 50;

    [Header("References")]
    [SerializeField] private TelemetryRecorder               telemetryRecorder;
    [SerializeField] private Carrom.Telemetry.PlaybackEngine  playbackEngine;
    [SerializeField] private CarromGameManager               carromGameManager;

    public void SetTelemetryRecorder(TelemetryRecorder r)            => telemetryRecorder = r;
    public void SetPlaybackEngine(Carrom.Telemetry.PlaybackEngine e) => playbackEngine    = e;
    public void SetCarromGameManager(CarromGameManager m)            => carromGameManager = m;

    // -------------------------------------------------------------------------
    // SHOT START — freeze the spectator's pieces
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called directly when the Host shoots (IsServer=true).
    /// Freezes the Client's pieces via ClientRpc, and also freezes the Host's
    /// own pieces if it is the spectator (never the case here, but kept symmetric).
    /// </summary>
    public void NotifyShotStart()
    {
        if (!IsServer) return;

        ulong activePlayerId = carromGameManager != null
            ? carromGameManager.GetActivePlayerClientId()
            : ulong.MaxValue;

        Debug.Log($"[BatchTransmitter] NotifyShotStart — active player: {activePlayerId}");
        FreezeSpectatorPiecesClientRpc(activePlayerId);

        // Freeze Host's own pieces if Host is the spectator
        if (NetworkManager.Singleton.LocalClientId != activePlayerId)
            SetSpectatorPiecesKinematic(true);
    }

    /// <summary>
    /// Called by the Client owner when it shoots.
    /// Asks the server to broadcast the freeze RPC to all peers.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestShotStartServerRpc(ServerRpcParams _ = default)
    {
        ulong activePlayerId = carromGameManager != null
            ? carromGameManager.GetActivePlayerClientId()
            : ulong.MaxValue;

        Debug.Log($"[BatchTransmitter] RequestShotStartServerRpc — active player: {activePlayerId}");
        FreezeSpectatorPiecesClientRpc(activePlayerId);

        // Freeze Host's own pieces — Host is the spectator when Client shoots
        if (NetworkManager.Singleton.LocalClientId != activePlayerId)
            SetSpectatorPiecesKinematic(true);
    }

    [ClientRpc]
    private void FreezeSpectatorPiecesClientRpc(ulong activePlayerId)
    {
        if (NetworkManager.Singleton.LocalClientId == activePlayerId)
        {
            Debug.Log("[BatchTransmitter] I am the active player — skipping freeze");
            return;
        }
        Debug.Log("[BatchTransmitter] I am the spectator — freezing pieces");
        SetSpectatorPiecesKinematic(true);
    }

    // -------------------------------------------------------------------------
    // SHOT END — transmit replay to the peer (two-way)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by the active player (Host or Client) after OnShotComplete.
    /// Routes the replay to the correct peer based on who is sending.
    /// </summary>
    public void TransmitFullReplay(EndStatePayload endState)
    {
        if (!IsSpawned) return;
        if (telemetryRecorder == null) { Debug.LogError("[BatchTransmitter] TelemetryRecorder is null"); return; }

        PhysicsFrame[] allFrames = telemetryRecorder.GetFullRecording();
        if (allFrames == null || allFrames.Length == 0)
        {
            Debug.LogWarning("[BatchTransmitter] No frames to transmit");
            return;
        }

        Debug.Log($"[BatchTransmitter] Transmitting {allFrames.Length} frames (IsServer={IsServer})");
        StartCoroutine(SendChunksCoroutine(allFrames, endState));
    }

    private IEnumerator SendChunksCoroutine(PhysicsFrame[] allFrames, EndStatePayload endState)
    {
        int totalChunks = Mathf.CeilToInt((float)allFrames.Length / FRAMES_PER_CHUNK);

        for (int i = 0; i < totalChunks; i++)
        {
            int start = i * FRAMES_PER_CHUNK;
            int count = Mathf.Min(FRAMES_PER_CHUNK, allFrames.Length - start);
            PhysicsFrame[] chunk = new PhysicsFrame[count];
            System.Array.Copy(allFrames, start, chunk, 0, count);

            if (IsServer)
                // Host → Client
                DeliverChunkToClientClientRpc(chunk, i, totalChunks);
            else
                // Client → Host
                DeliverChunkToServerServerRpc(chunk, i, totalChunks);

            Debug.Log($"[BatchTransmitter] Sent chunk {i + 1}/{totalChunks} ({count} frames)");
            yield return null;
        }

        if (IsServer)
            DeliverEndStateToClientClientRpc(endState);
        else
            DeliverEndStateToServerServerRpc(endState);

        Debug.Log("[BatchTransmitter] All chunks + end state transmitted.");
    }

    // -------------------------------------------------------------------------
    // HOST → CLIENT delivery
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void DeliverChunkToClientClientRpc(PhysicsFrame[] chunkFrames, int chunkIndex, int totalChunks)
    {
        if (IsServer) return; // Host sent this — only Client receives
        Debug.Log($"[BatchTransmitter] CLIENT received chunk {chunkIndex + 1}/{totalChunks}");
        playbackEngine?.ReceiveChunk(chunkFrames, chunkIndex, totalChunks);
    }

    [ClientRpc]
    private void DeliverEndStateToClientClientRpc(EndStatePayload endState)
    {
        if (IsServer) return;
        Debug.Log("[BatchTransmitter] CLIENT received end state");
        playbackEngine?.ReceiveEndState(endState);
    }

    // -------------------------------------------------------------------------
    // CLIENT → HOST delivery
    // -------------------------------------------------------------------------

    [ServerRpc(RequireOwnership = false)]
    private void DeliverChunkToServerServerRpc(PhysicsFrame[] chunkFrames, int chunkIndex, int totalChunks, ServerRpcParams _ = default)
    {
        Debug.Log($"[BatchTransmitter] HOST received chunk {chunkIndex + 1}/{totalChunks}");
        playbackEngine?.ReceiveChunk(chunkFrames, chunkIndex, totalChunks);
    }

    [ServerRpc(RequireOwnership = false)]
    private void DeliverEndStateToServerServerRpc(EndStatePayload endState, ServerRpcParams _ = default)
    {
        Debug.Log("[BatchTransmitter] HOST received end state");
        playbackEngine?.ReceiveEndState(endState);
    }

    // -------------------------------------------------------------------------
    // PLAYBACK COMPLETE → transfer authority
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by PlaybackEngine on the spectator when replay finishes.
    /// If spectator is the Client, it sends a ServerRpc.
    /// If spectator is the Host, it calls TriggerAuthorityTransfer directly.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void NotifyEndStateAppliedServerRpc(ServerRpcParams _ = default)
    {
        Debug.Log("[BatchTransmitter] Spectator finished playback — transferring authority");
        carromGameManager?.TriggerAuthorityTransfer();
    }

    // -------------------------------------------------------------------------
    // Piece physics state (NO NetworkTransform — Path A)
    // -------------------------------------------------------------------------

    public void SetSpectatorPiecesKinematic(bool kinematic)
    {
        string[] tags = { "Black", "White", "Queen", "Striker" };
        int count = 0;
        foreach (string tag in tags)
            foreach (GameObject go in GameObject.FindGameObjectsWithTag(tag))
            {
                Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
                if (rb == null) continue;

                if (!kinematic)
                {
                    rb.linearVelocity  = Vector2.zero;
                    rb.angularVelocity = 0f;
                }

                rb.bodyType      = kinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
                rb.simulated     = true;
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
                count++;
            }
        Debug.Log($"[BatchTransmitter] {count} pieces → {(kinematic ? "Kinematic" : "Dynamic+ZeroVelocity")}");
    }
}
