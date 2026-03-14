using Unity.Netcode;

/// <summary>
/// Authoritative final state for all game pieces after a shot completes.
/// Contains final positions and rotations for synchronization.
/// </summary>
public struct EndStatePayload : INetworkSerializable
{
    public int pieceCount;
    public PieceState[] finalStates;

    public EndStatePayload(int maxPieces = 20)
    {
        pieceCount = 0;
        finalStates = new PieceState[maxPieces];
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref pieceCount);

        if (serializer.IsWriter)
        {
            for (int i = 0; i < pieceCount; i++)
            {
                finalStates[i].NetworkSerialize(serializer);
            }
        }
        else
        {
            // Ensure array is allocated on read
            if (finalStates == null || finalStates.Length < pieceCount)
            {
                finalStates = new PieceState[20];
            }

            for (int i = 0; i < pieceCount; i++)
            {
                finalStates[i].NetworkSerialize(serializer);
            }
        }
    }
}
