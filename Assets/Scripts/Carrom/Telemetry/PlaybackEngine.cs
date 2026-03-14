using System.Collections.Generic;
using UnityEngine;

namespace Carrom.Telemetry
{
    /// <summary>
    /// Assembles incoming replay chunks into a complete recording,
    /// then plays back in FixedUpdate using Rigidbody2D.MovePosition for
    /// perfectly smooth, physics-rate animation on the spectator's screen.
    ///
    /// Pieces MUST be: bodyType=Kinematic, interpolation=Interpolate during playback.
    /// NT and NRB2D have been removed from all prefabs (Path A).
    /// ApplyEndState uses direct transform assignment + velocity wipe — no NT/Teleport.
    /// </summary>
    public class PlaybackEngine : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PieceRegistry    pieceRegistry;
        [SerializeField] private BatchTransmitter batchTransmitter;

        // Download assembly
        private PhysicsFrame[]  downloadedReplay;
        private int             receivedChunks;
        private int             totalChunks;
        private bool            downloadComplete;
        private EndStatePayload pendingEndState;
        private bool            hasEndState;

        // Playback state
        private int  currentIndex;
        private bool isPlaying;

        public bool IsPlaying => isPlaying;

        public void SetPieceRegistry(PieceRegistry r) => pieceRegistry = r;

        // Temporary list used during chunk assembly
        private List<PhysicsFrame[]> chunkList = new List<PhysicsFrame[]>();

        // -------------------------------------------------------------------------
        // CHUNK ASSEMBLY
        // -------------------------------------------------------------------------

        public void ReceiveChunk(PhysicsFrame[] chunkFrames, int chunkIndex, int totalChunksCount)
        {
            if (chunkIndex == 0)
            {
                downloadedReplay = null;
                receivedChunks   = 0;
                totalChunks      = totalChunksCount;
                downloadComplete = false;
                hasEndState      = false;
                isPlaying        = false;
                currentIndex     = 0;
                chunkList.Clear();
            }

            chunkList.Add(chunkFrames);
            receivedChunks++;

            Debug.Log($"[PlaybackEngine] Assembled chunk {receivedChunks}/{totalChunks}");
            TryStartPlayback();
        }

        public void ReceiveEndState(EndStatePayload endState)
        {
            pendingEndState = endState;
            hasEndState     = true;
            Debug.Log("[PlaybackEngine] End state received");
            TryStartPlayback();
        }

        private void TryStartPlayback()
        {
            if (receivedChunks < totalChunks || !hasEndState) return;
            if (isPlaying) return;

            int totalFrames = 0;
            foreach (var c in chunkList) totalFrames += c.Length;

            downloadedReplay = new PhysicsFrame[totalFrames];
            int offset = 0;
            foreach (var c in chunkList)
            {
                System.Array.Copy(c, 0, downloadedReplay, offset, c.Length);
                offset += c.Length;
            }
            chunkList.Clear();

            currentIndex     = 0;
            downloadComplete = true;
            isPlaying        = true;

            Debug.Log($"[PlaybackEngine] Download complete — starting playback of {totalFrames} frames");
        }

        // -------------------------------------------------------------------------
        // FIXEDUPDATE PLAYBACK — LOCKED, DO NOT MODIFY
        // -------------------------------------------------------------------------

        private void FixedUpdate()
        {
            if (!isPlaying || downloadedReplay == null) return;

            if (currentIndex >= downloadedReplay.Length)
            {
                FinishPlayback();
                return;
            }

            PhysicsFrame frame = downloadedReplay[currentIndex];

            for (int i = 0; i < frame.pieceCount; i++)
            {
                PieceState  state = frame.pieces[i];
                GameObject  piece = pieceRegistry?.GetPiece(state.pieceId);
                if (piece == null) continue;

                Rigidbody2D rb = piece.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.MovePosition(new Vector2(state.xPosition, state.yPosition));
                    rb.MoveRotation(state.zRotation);
                }
            }

            currentIndex++;
        }

        // -------------------------------------------------------------------------
        // FINISH
        // -------------------------------------------------------------------------

        private void FinishPlayback()
        {
            isPlaying = false;
            Debug.Log("[PlaybackEngine] Playback complete — applying end state");

            // 1. Hard-snap all pieces to their authoritative final positions and wipe velocity
            ApplyEndState(pendingEndState);

            // 2. Restore pieces to Dynamic (velocity already zeroed inside ApplyEndState,
            //    but SetSpectatorPiecesKinematic also zeroes as a safety net)
            batchTransmitter?.SetSpectatorPiecesKinematic(false);

            // 3. Tell server to transfer authority
            batchTransmitter?.NotifyEndStateAppliedServerRpc();
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

                // Direct transform assignment — authoritative, instant, no interpolation
                piece.transform.position = new Vector3(s.xPosition, s.yPosition, piece.transform.position.z);
                piece.transform.rotation = Quaternion.Euler(0f, 0f, s.zRotation);

                // Zero velocity so physics solver starts from rest when body goes Dynamic
                Rigidbody2D rb = piece.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity  = Vector2.zero;
                    rb.angularVelocity = 0f;
                }
            }

            Debug.Log($"[PlaybackEngine] End state applied — {payload.pieceCount} pieces snapped + velocity zeroed");
        }

        public void StopPlayback()
        {
            isPlaying        = false;
            currentIndex     = 0;
            downloadedReplay = null;
        }
    }
}
