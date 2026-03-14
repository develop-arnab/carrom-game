using Unity.Netcode;

/// <summary>
/// Full-precision state of a single game piece at one FixedUpdate tick.
/// Exactly 13 bytes: 1 (pieceId) + 4 (x) + 4 (y) + 4 (zRotation).
/// 20 pieces × 13 bytes = 260 bytes per frame.
/// 50 frames per chunk = 13,000 bytes (~13KB) — well under the 64KB RPC limit.
/// </summary>
public struct PieceState : INetworkSerializable
{
    public byte  pieceId;
    public float xPosition;
    public float yPosition;
    public float zRotation;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref pieceId);
        serializer.SerializeValue(ref xPosition);
        serializer.SerializeValue(ref yPosition);
        serializer.SerializeValue(ref zRotation);
    }
}
