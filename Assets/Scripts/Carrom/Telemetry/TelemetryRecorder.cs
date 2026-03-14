using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records ALL 20 pieces every FixedUpdate tick for the duration of the shot.
/// No filtering. No compression. Mathematically perfect capture.
/// Each frame = 260 bytes. A 5-second shot at 50Hz = 250 frames = 65KB total.
/// Chunked transmission handles the 64KB RPC limit.
/// </summary>
public class TelemetryRecorder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PieceRegistry pieceRegistry;

    private List<PhysicsFrame> fullShotRecording = new List<PhysicsFrame>(512);
    private bool isRecording;

    public bool IsRecording  => isRecording;
    public int  FrameCount   => fullShotRecording.Count;

    public void SetPieceRegistry(PieceRegistry r) => pieceRegistry = r;

    private void FixedUpdate()
    {
        if (isRecording) CaptureFrame();
    }

    public void StartRecording()
    {
        fullShotRecording.Clear();
        isRecording = true;
        Debug.Log("[TelemetryRecorder] Recording started");
    }

    public void StopRecording()
    {
        isRecording = false;
        Debug.Log($"[TelemetryRecorder] Recording stopped — {fullShotRecording.Count} frames captured");
    }

    /// <summary>
    /// Unconditionally records all 20 pieces every tick.
    /// </summary>
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

    /// <summary>Returns the complete recording as an array for chunked transmission.</summary>
    public PhysicsFrame[] GetFullRecording() => fullShotRecording.ToArray();
}
