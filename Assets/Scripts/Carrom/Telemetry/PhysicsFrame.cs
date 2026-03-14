using Unity.Netcode;

/// <summary>
/// Snapshot of all 20 pieces at one FixedUpdate tick.
/// pieceCount is always 20 when recorded by TelemetryRecorder.
/// 20 × 13 bytes = 260 bytes per frame.
/// </summary>
public struct PhysicsFrame : INetworkSerializable
{
    public int        pieceCount;
    public PieceState[] pieces; // always length 20

    public PhysicsFrame(int maxPieces = 20)
    {
        pieceCount = 0;
        pieces     = new PieceState[maxPieces];
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref pieceCount);

        if (serializer.IsWriter)
        {
            for (int i = 0; i < pieceCount; i++)
                pieces[i].NetworkSerialize(serializer);
        }
        else
        {
            if (pieces == null || pieces.Length < pieceCount)
                pieces = new PieceState[20];
            for (int i = 0; i < pieceCount; i++)
                pieces[i].NetworkSerialize(serializer);
        }
    }
}
