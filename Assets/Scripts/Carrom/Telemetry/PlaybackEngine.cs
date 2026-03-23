using System.Collections.Generic;
using UnityEngine;

namespace Carrom.Telemetry
{
    /// <summary>
    /// Live Streaming Receiver. Ingests PhysicsFrames into a Queue as they arrive
    /// and plays them back in FixedUpdate using a 2-chunk (100-frame) safety buffer.
    ///
    /// State machine:
    ///   Idle      → waiting for first chunk
    ///   Buffering → accumulating frames until buffer threshold or end state arrives
    ///   Playing   → dequeuing one frame per FixedUpdate tick
    ///
    /// Pieces MUST be: bodyType=Kinematic, interpolation=Interpolate during playback.
    /// NT and NRB2D removed from all prefabs (Path A).
    /// </summary>
    public class PlaybackEngine : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PieceRegistry    pieceRegistry;
        [SerializeField] private BatchTransmitter batchTransmitter;
        [SerializeField] private AudioClip        collisionClip;

        private const int BUFFER_THRESHOLD = 40; // 2 chunks — ~2s at 50Hz

        // -------------------------------------------------------------------------
        // STATE MACHINE
        // -------------------------------------------------------------------------

        private enum PlaybackState { Idle, Buffering, Playing }
        private PlaybackState state = PlaybackState.Idle;

        // Live frame queue
        private Queue<PhysicsFrame> frameQueue = new Queue<PhysicsFrame>();

        // Chunk tracking
        private int  receivedChunks;
        private int  totalChunks;
        private int  currentIndex;

        // End state
        private EndStatePayload pendingEndState;
        private bool            hasEndState;

        // Persistent patch cache — stores server overrides that may arrive before the payload.
        // Applied immediately if payload exists, or deferred until ReceiveEndState fires.
        private readonly Dictionary<byte, Vector3> pendingPatches = new Dictionary<byte, Vector3>();

        // Audio track
        private ReplayAudioEvent[] audioTrack;
        private int                audioTrackIndex;
        private bool               hasAudioTrack;

        // Graveyard detection — pocket sound for spectator
        private const float GraveyardThreshold = 900f;
        private AudioSource pocketAudioSource;

        public bool IsPlaying => state == PlaybackState.Playing;

        public void SetPieceRegistry(PieceRegistry r) => pieceRegistry = r;

        private void Start()
        {
            BoardScript board = FindObjectOfType<BoardScript>();
            if (board != null) pocketAudioSource = board.GetComponent<AudioSource>();
        }

        // -------------------------------------------------------------------------
        // CHUNK INGESTION
        // -------------------------------------------------------------------------

        public void ReceiveChunk(PhysicsFrame[] chunkFrames, int chunkIndex, int totalChunksCount)
        {
            if (chunkIndex == 0)
            {
                frameQueue.Clear();
                state           = PlaybackState.Buffering;
                receivedChunks  = 0;
                currentIndex    = 0;
                totalChunks     = 0;
                hasEndState     = false;
                // Audio state intentionally NOT reset here — audio slices may arrive
                // before Chunk 0 due to network ordering. Wiped in FinishPlayback/StopPlayback.
            }

            foreach (PhysicsFrame frame in chunkFrames)
                frameQueue.Enqueue(frame);

            receivedChunks++;

            // totalChunksCount == 0 → live chunk, more coming
            // totalChunksCount  > 0 → final sweep chunk, real total now known
            if (totalChunksCount > 0)
                totalChunks = totalChunksCount;

            TryStartPlayback();
        }

        public void ReceiveEndState(EndStatePayload endState)
        {
            pendingEndState = endState;
            hasEndState     = true;
            // Apply any patches that arrived before the payload (pre-emptive RPC race condition)
            foreach (var kvp in pendingPatches)
                PatchEndState(kvp.Key, kvp.Value);
            TryStartPlayback();
        }

        public void ReceiveAudioTrack(ReplayAudioEvent[] track)
        {
            // Arrives in incremental slices — append rather than replace
            if (audioTrack == null)
            {
                audioTrack      = track;
                audioTrackIndex = 0;
                hasAudioTrack   = true;
            }
            else
            {
                int oldLen = audioTrack.Length;
                ReplayAudioEvent[] merged = new ReplayAudioEvent[oldLen + track.Length];
                System.Array.Copy(audioTrack, merged, oldLen);
                System.Array.Copy(track, 0, merged, oldLen, track.Length);
                audioTrack    = merged;
                hasAudioTrack = true;
            }
        }

        // -------------------------------------------------------------------------
        // BUFFER GATEKEEPER
        // -------------------------------------------------------------------------

        private void TryStartPlayback()
        {
            if (state != PlaybackState.Buffering) return;

            // Start playing once buffer is full OR the shot is already over (short shot)
            if (frameQueue.Count >= BUFFER_THRESHOLD || hasEndState)
            {
                state = PlaybackState.Playing;
                Debug.Log($"[PlaybackEngine] Buffer filled ({frameQueue.Count} frames). Live playback started!");
            }
        }

        // -------------------------------------------------------------------------
        // FIXEDUPDATE PLAYBACK
        // -------------------------------------------------------------------------

        private void FixedUpdate()
        {
            if (state != PlaybackState.Playing) return;

            if (frameQueue.Count > 0)
            {
                PhysicsFrame frame = frameQueue.Dequeue();

                for (int i = 0; i < frame.pieceCount; i++)
                {
                    PieceState  pieceState = frame.pieces[i];
                    GameObject  piece      = pieceRegistry?.GetPiece(pieceState.pieceId);
                    if (piece == null) continue;

                    Rigidbody2D rb = piece.GetComponent<Rigidbody2D>();
                    if (rb != null)
                    {
                        SpriteRenderer sr = piece.GetComponent<SpriteRenderer>();

                        // 1. Enter Graveyard (Latch Open) — sr.enabled ensures exactly one ghost per pocketing event
                        if (pieceState.xPosition >= GraveyardThreshold && sr != null && sr.enabled)
                        {
                            pocketAudioSource?.Play();
                            Vector3 exactPocketCenter = new Vector3(
                                pieceState.xPosition - 1000f,
                                pieceState.yPosition - 1000f,
                                0f);
                            SpawnGhostCoin(sr.sprite, piece.transform.position, exactPocketCenter, piece.transform.localScale);
                            sr.enabled = false; // close the latch
                        }
                        // 2. Exit Graveyard (e.g. striker reset for new turn)
                        else if (pieceState.xPosition < GraveyardThreshold && sr != null && !sr.enabled)
                        {
                            sr.enabled = true; // re-open the latch
                        }

                        rb.MovePosition(new Vector2(pieceState.xPosition, pieceState.yPosition));
                        rb.MoveRotation(pieceState.zRotation);
                    }
                }

                currentIndex++;

                // Audio track playback — fire any events whose frameIndex matches the current visual frame.
                // Loop handles multiple sounds on the same frame (e.g. striker hits two coins simultaneously).
                if (hasAudioTrack && audioTrack != null && collisionClip != null)
                {
                    while (audioTrackIndex < audioTrack.Length &&
                           audioTrack[audioTrackIndex].frameIndex <= currentIndex)
                    {
                        ReplayAudioEvent evt = audioTrack[audioTrackIndex];
                        AudioSource.PlayClipAtPoint(collisionClip,
                            new Vector3(evt.position.x, evt.position.y, 0f),
                            evt.volume);
                        audioTrackIndex++;
                    }
                }
            }
            else
            {
                // Queue drained
                if (hasEndState)
                {
                    state = PlaybackState.Idle;
                    FinishPlayback();
                }
                else
                {
                    // Network lag underflow — pause and re-buffer
                    state = PlaybackState.Buffering;
                    Debug.LogWarning("[PlaybackEngine] Network lag! Buffering...");
                }
            }
        }

        // -------------------------------------------------------------------------
        // FINISH — untouched
        // -------------------------------------------------------------------------

        private void FinishPlayback()
        {
            Debug.Log("[PlaybackEngine] Playback complete — applying end state");
            ApplyEndState(pendingEndState);
            batchTransmitter?.SetSpectatorPiecesKinematic(false);
            batchTransmitter?.NotifyEndStateAppliedServerRpc();

            // Wipe audio state here — safe to do after playback is fully complete
            audioTrack      = null;
            audioTrackIndex = 0;
            hasAudioTrack   = false;
            pendingPatches.Clear();
        }

        /// <summary>
        /// Hard-snaps every piece to its authoritative position/rotation and zeroes
        /// its Rigidbody velocity. No NT/Teleport — Path A uses direct assignment only.
        /// </summary>
        public void ApplyEndState(EndStatePayload payload)
        {
            if (pieceRegistry == null || payload.pieceCount == 0) return;

            for (int i = 0; i < payload.pieceCount; i++)
            {
                PieceState s     = payload.finalStates[i];
                GameObject piece = pieceRegistry.GetPiece(s.pieceId);
                if (piece == null) continue;

                piece.transform.position = new Vector3(s.xPosition, s.yPosition, piece.transform.position.z);
                piece.transform.rotation = Quaternion.Euler(0f, 0f, s.zRotation);

                Rigidbody2D rb = piece.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity  = Vector2.zero;
                    rb.angularVelocity = 0f;
                }
            }

            Debug.Log($"[PlaybackEngine] End state applied — {payload.pieceCount} pieces snapped + velocity zeroed");
        }

        /// <summary>
        /// Overwrites a single piece's position inside the pending EndStatePayload.
        /// Called by CarromGameManager when the Queen is respawned mid-replay so that
        /// ApplyEndState() snaps the Queen to the correct position when the replay ends.
        /// </summary>
        public void PatchEndState(byte targetPieceId, Vector3 overridePosition)
        {
            // Always cache — handles the case where the patch arrives before the payload
            pendingPatches[targetPieceId] = overridePosition;

            if (!hasEndState) return;

            for (int i = 0; i < pendingEndState.pieceCount; i++)
            {
                if (pendingEndState.finalStates[i].pieceId == targetPieceId)
                {
                    pendingEndState.finalStates[i].xPosition = overridePosition.x;
                    pendingEndState.finalStates[i].yPosition = overridePosition.y;
                    Debug.Log($"[PlaybackEngine] EndState patched — piece {targetPieceId} → ({overridePosition.x}, {overridePosition.y})");
                    return;
                }
            }
        }

        public void StopPlayback()        {
            state        = PlaybackState.Idle;
            currentIndex = 0;
            frameQueue.Clear();
            audioTrack      = null;
            audioTrackIndex = 0;
            hasAudioTrack   = false;
            pendingPatches.Clear();
        }

        // -------------------------------------------------------------------------
        // GHOST COIN — untouched
        // -------------------------------------------------------------------------

        private void SpawnGhostCoin(Sprite originalSprite, Vector3 entryPosition, Vector3 pocketCenter, Vector3 originalScale)
        {
            if (originalSprite == null) return;
            GameObject ghost         = new GameObject("GhostCoin");
            ghost.transform.position = entryPosition;
            SpriteRenderer ghostSr   = ghost.AddComponent<SpriteRenderer>();
            ghostSr.sprite           = originalSprite;
            ghostSr.sortingOrder     = 10;
            StartCoroutine(AnimateGhostCoin(ghost, ghostSr, entryPosition, pocketCenter, originalScale));
        }

        private System.Collections.IEnumerator AnimateGhostCoin(GameObject ghost, SpriteRenderer ghostSr, Vector3 entryPosition, Vector3 pocketCenter, Vector3 originalScale)
        {
            float      duration   = 0.55f;
            float      elapsed    = 0f;
            Vector3    startScale = originalScale;
            Vector3    endScale   = Vector3.one * 0.08f;
            Color      baseColor  = ghostSr.color;
            Color      startColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.35f);
            Color      endColor   = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            ghostSr.color         = startColor;
            Quaternion startRot   = ghost.transform.rotation;
            Quaternion endRot     = startRot * Quaternion.Euler(60f, 0f, UnityEngine.Random.Range(-15f, 15f));

            while (elapsed < duration)
            {
                float t     = elapsed / duration;
                float easeT = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic
                ghost.transform.position   = Vector3.Lerp(entryPosition, pocketCenter, easeT);
                ghost.transform.localScale = Vector3.Lerp(startScale, endScale, t);
                ghost.transform.rotation   = Quaternion.Slerp(startRot, endRot, easeT);
                ghostSr.color              = Color.Lerp(startColor, endColor, t);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Destroy(ghost);
        }
    }
}
