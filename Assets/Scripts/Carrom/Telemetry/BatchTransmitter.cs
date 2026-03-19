using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Symmetrical two-way live-streaming telemetry pipeline.
///
/// While the shot is in progress, Update() harvests completed 50-frame chunks
/// from TelemetryRecorder and streams them immediately to the spectator.
/// When OnShotComplete fires, TransmitFullReplay does a final sweep for any
/// remaining frames, remaining audio events, and the EndStatePayload.
///
/// Chunk protocol:
///   totalChunks == 0  → live streaming chunk, more coming
///   totalChunks  > 0  → final sweep chunk, receiver now knows the real total
///
/// NT and NRB2D removed from all prefabs (Path A). Zero NetworkTransform references.
/// </summary>
public class BatchTransmitter : NetworkBehaviour
{
    private const int FRAMES_PER_CHUNK = 20;

    [Header("References")]
    [SerializeField] private TelemetryRecorder               telemetryRecorder;
    [SerializeField] private Carrom.Telemetry.PlaybackEngine  playbackEngine;
    [SerializeField] private CarromGameManager               carromGameManager;

    public void SetTelemetryRecorder(TelemetryRecorder r)            => telemetryRecorder = r;
    public void SetPlaybackEngine(Carrom.Telemetry.PlaybackEngine e) => playbackEngine    = e;
    public void SetCarromGameManager(CarromGameManager m)            => carromGameManager = m;

    // -------------------------------------------------------------------------
    // LIVE STREAMING STATE
    // -------------------------------------------------------------------------

    private int  lastSentFrameIndex = 0;
    private int  currentChunkIndex  = 0;
    private int  lastSentAudioIndex = 0;
    private bool isLiveStreaming     = false;

    private void ResetCursors()
    {
        lastSentFrameIndex = 0;
        currentChunkIndex  = 0;
        lastSentAudioIndex = 0;
        isLiveStreaming    = true;
    }

    // -------------------------------------------------------------------------
    // SHOT START — freeze spectator + start streaming
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called locally by the active player (Host OR Client) at shot start.
    /// Resets streaming cursors here — on the machine that owns the physics.
    /// Then routes the spectator freeze to the correct peer.
    /// </summary>
    public void StartShotAsActivePlayer()
    {
        ResetCursors(); // always local — never runs on the wrong machine

        if (IsServer)
        {
            ulong activePlayerId = carromGameManager != null
                ? carromGameManager.GetActivePlayerClientId()
                : ulong.MaxValue;
            FreezeSpectatorPiecesClientRpc(activePlayerId);
        }
        else
        {
            RequestShotStartServerRpc();
        }
    }

    public void NotifyShotStart()
    {
        if (!IsServer) return;

        ulong activePlayerId = carromGameManager != null
            ? carromGameManager.GetActivePlayerClientId()
            : ulong.MaxValue;

        FreezeSpectatorPiecesClientRpc(activePlayerId);

        if (NetworkManager.Singleton.LocalClientId != activePlayerId)
            SetSpectatorPiecesKinematic(true);
        // NOTE: ResetCursors() intentionally removed — called by StartShotAsActivePlayer instead
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestShotStartServerRpc(ServerRpcParams _ = default)
    {
        ulong activePlayerId = carromGameManager != null
            ? carromGameManager.GetActivePlayerClientId()
            : ulong.MaxValue;

        FreezeSpectatorPiecesClientRpc(activePlayerId);

        if (NetworkManager.Singleton.LocalClientId != activePlayerId)
            SetSpectatorPiecesKinematic(true);
        // NOTE: ResetCursors() intentionally removed — runs on Host, not the active Client
    }

    [ClientRpc]
    private void FreezeSpectatorPiecesClientRpc(ulong activePlayerId)
    {
        if (NetworkManager.Singleton.LocalClientId == activePlayerId) return;
        SetSpectatorPiecesKinematic(true);
    }

    // -------------------------------------------------------------------------
    // UPDATE — live harvester
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!IsSpawned || !isLiveStreaming || telemetryRecorder == null) return;

        // Visual harvesting — send a chunk every time 50 new frames are ready
        while (telemetryRecorder.FrameCount - lastSentFrameIndex >= FRAMES_PER_CHUNK)
        {
            PhysicsFrame[] allFrames = telemetryRecorder.GetFullRecording();
            PhysicsFrame[] chunk     = new PhysicsFrame[FRAMES_PER_CHUNK];
            System.Array.Copy(allFrames, lastSentFrameIndex, chunk, 0, FRAMES_PER_CHUNK);

            // totalChunks = 0 signals "live chunk, more coming"
            if (IsServer)
                DeliverChunkToClientClientRpc(chunk, currentChunkIndex, 0);
            else
                DeliverChunkToServerServerRpc(chunk, currentChunkIndex, 0);

            lastSentFrameIndex += FRAMES_PER_CHUNK;
            currentChunkIndex++;
        }

        // Audio harvesting — send any new events that arrived since last check
        if (telemetryRecorder.audioTrack.Count > lastSentAudioIndex)
        {
            int newCount = telemetryRecorder.audioTrack.Count - lastSentAudioIndex;
            ReplayAudioEvent[] slice = new ReplayAudioEvent[newCount];
            telemetryRecorder.audioTrack.CopyTo(lastSentAudioIndex, slice, 0, newCount);

            if (IsServer)
                DeliverAudioTrackToClientClientRpc(slice);
            else
                DeliverAudioTrackToServerServerRpc(slice);

            lastSentAudioIndex = telemetryRecorder.audioTrack.Count;
        }
    }

    // -------------------------------------------------------------------------
    // SHOT END — final sweep
    // -------------------------------------------------------------------------

    public void TransmitFullReplay(EndStatePayload endState)
    {
        if (!IsSpawned) return;
        if (telemetryRecorder == null) { Debug.LogError("[BatchTransmitter] TelemetryRecorder is null"); return; }

        isLiveStreaming = false;

        PhysicsFrame[] allFrames    = telemetryRecorder.GetFullRecording();
        int            remaining    = allFrames != null ? allFrames.Length - lastSentFrameIndex : 0;
        int            finalTotal   = currentChunkIndex + (remaining > 0 ? 1 : 0);

        // If no chunks were ever sent (very short shot), finalTotal must be at least 1
        if (finalTotal == 0) finalTotal = 1;

        if (remaining > 0)
        {
            PhysicsFrame[] lastChunk = new PhysicsFrame[remaining];
            System.Array.Copy(allFrames, lastSentFrameIndex, lastChunk, 0, remaining);

            // finalTotal > 0 signals "this is the last chunk"
            if (IsServer)
                DeliverChunkToClientClientRpc(lastChunk, currentChunkIndex, finalTotal);
            else
                DeliverChunkToServerServerRpc(lastChunk, currentChunkIndex, finalTotal);
        }
        else
        {
            // No leftover frames — but receiver still needs to know the final total.
            // Re-send the last chunk index with the real totalChunks so TryStartPlayback fires.
            // Edge case: zero chunks sent at all (instant pocket). Send an empty final chunk.
            PhysicsFrame[] empty = new PhysicsFrame[0];
            if (IsServer)
                DeliverChunkToClientClientRpc(empty, currentChunkIndex, finalTotal);
            else
                DeliverChunkToServerServerRpc(empty, currentChunkIndex, finalTotal);
        }

        // Flush any remaining audio events
        if (telemetryRecorder.audioTrack.Count > lastSentAudioIndex)
        {
            int newCount = telemetryRecorder.audioTrack.Count - lastSentAudioIndex;
            ReplayAudioEvent[] slice = new ReplayAudioEvent[newCount];
            telemetryRecorder.audioTrack.CopyTo(lastSentAudioIndex, slice, 0, newCount);

            if (IsServer)
                DeliverAudioTrackToClientClientRpc(slice);
            else
                DeliverAudioTrackToServerServerRpc(slice);
        }

        // End state — triggers TryStartPlayback on the receiver
        if (IsServer)
            DeliverEndStateToClientClientRpc(endState);
        else
            DeliverEndStateToServerServerRpc(endState);

        Debug.Log($"[BatchTransmitter] Final sweep complete — {finalTotal} total chunks, end state sent.");
    }

    // -------------------------------------------------------------------------
    // HOST → CLIENT delivery
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void DeliverChunkToClientClientRpc(PhysicsFrame[] chunkFrames, int chunkIndex, int totalChunks)
    {
        if (IsServer) return;
        playbackEngine?.ReceiveChunk(chunkFrames, chunkIndex, totalChunks);
    }

    [ClientRpc]
    private void DeliverAudioTrackToClientClientRpc(ReplayAudioEvent[] audioTrack)
    {
        if (IsServer) return;
        playbackEngine?.ReceiveAudioTrack(audioTrack);
    }

    [ClientRpc]
    private void DeliverEndStateToClientClientRpc(EndStatePayload endState)
    {
        if (IsServer) return;
        playbackEngine?.ReceiveEndState(endState);
    }

    // -------------------------------------------------------------------------
    // CLIENT → HOST delivery
    // -------------------------------------------------------------------------

    [ServerRpc(RequireOwnership = false)]
    private void DeliverChunkToServerServerRpc(PhysicsFrame[] chunkFrames, int chunkIndex, int totalChunks, ServerRpcParams _ = default)
    {
        playbackEngine?.ReceiveChunk(chunkFrames, chunkIndex, totalChunks);
    }

    [ServerRpc(RequireOwnership = false)]
    private void DeliverAudioTrackToServerServerRpc(ReplayAudioEvent[] audioTrack, ServerRpcParams _ = default)
    {
        playbackEngine?.ReceiveAudioTrack(audioTrack);
    }

    [ServerRpc(RequireOwnership = false)]
    private void DeliverEndStateToServerServerRpc(EndStatePayload endState, ServerRpcParams _ = default)
    {
        playbackEngine?.ReceiveEndState(endState);
    }

    // -------------------------------------------------------------------------
    // PLAYBACK COMPLETE → transfer authority
    // -------------------------------------------------------------------------

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
