using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records ALL 20 pieces every FixedUpdate tick for the duration of the shot.
/// Also maintains a decoupled audio track: collision events broadcast by
/// CollisionSoundManager and StrikerController are logged with their frame index
/// so the spectator's PlaybackEngine can play them at the exact right moment.
/// </summary>
public class TelemetryRecorder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PieceRegistry pieceRegistry;

    // Visual track
    private List<PhysicsFrame> fullShotRecording = new List<PhysicsFrame>(512);

    // Audio track — public so BatchTransmitter can read it live during streaming
    public List<ReplayAudioEvent> audioTrack { get; private set; } = new List<ReplayAudioEvent>(64);

    private bool isRecording;

    public bool IsRecording => isRecording;
    public int  FrameCount  => fullShotRecording.Count;

    public void SetPieceRegistry(PieceRegistry r) => pieceRegistry = r;

    // -------------------------------------------------------------------------
    // SUBSCRIBE / UNSUBSCRIBE
    // -------------------------------------------------------------------------

    private void OnEnable()
    {
        CollisionSoundManager.OnCollisionSoundPlayed += OnCollisionSound;
    }

    private void OnDisable()
    {
        CollisionSoundManager.OnCollisionSoundPlayed -= OnCollisionSound;
    }

    // -------------------------------------------------------------------------
    // RECORDING CONTROL
    // -------------------------------------------------------------------------

    public void StartRecording()
    {
        fullShotRecording.Clear();
        audioTrack.Clear();
        isRecording = true;
        Debug.Log("[TelemetryRecorder] Recording started");
    }

    public void StopRecording()
    {
        isRecording = false;
        Debug.Log($"[TelemetryRecorder] Recording stopped — {fullShotRecording.Count} visual frames, {audioTrack.Count} audio events");
    }

    // -------------------------------------------------------------------------
    // VISUAL CAPTURE — LOCKED, runs every FixedUpdate tick
    // -------------------------------------------------------------------------

    private void FixedUpdate()
    {
        if (isRecording) CaptureFrame();
    }

    private void CaptureFrame()
    {
        if (pieceRegistry == null) return;

        PhysicsFrame frame = new PhysicsFrame(20);
        frame.pieceCount = 0;

        for (byte id = 0; id < 20; id++)
        {
            GameObject piece = pieceRegistry.GetPiece(id);
            if (piece == null) continue;

            frame.pieces[frame.pieceCount++] = new PieceState
            {
                pieceId   = id,
                xPosition = piece.transform.position.x,
                yPosition = piece.transform.position.y,
                zRotation = piece.transform.eulerAngles.z
            };
        }

        fullShotRecording.Add(frame);
    }

    // -------------------------------------------------------------------------
    // AUDIO CAPTURE — fires on collision broadcast
    // -------------------------------------------------------------------------

    private void OnCollisionSound(Vector2 position, float volume)
    {
        if (!isRecording) return;

        audioTrack.Add(new ReplayAudioEvent
        {
            frameIndex = fullShotRecording.Count,
            position   = position,
            volume     = volume
        });
    }

    // -------------------------------------------------------------------------
    // RETRIEVAL
    // -------------------------------------------------------------------------

    public PhysicsFrame[]     GetFullRecording() => fullShotRecording.ToArray();
    public ReplayAudioEvent[] GetAudioTrack()    => audioTrack.ToArray();
}
