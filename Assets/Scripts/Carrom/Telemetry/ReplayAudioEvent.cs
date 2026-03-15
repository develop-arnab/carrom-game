using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A single collision sound event captured during the active player's shot.
/// Transmitted as a lightweight "audio track" alongside the visual replay chunks.
/// frameIndex: which visual frame this sound belongs to (matches downloadedReplay index).
/// position:   world-space impact point so AudioSource.PlayClipAtPoint places it correctly.
/// volume:     pre-calculated from impact speed so the spectator hears the same intensity.
/// </summary>
public struct ReplayAudioEvent : INetworkSerializable
{
    public int     frameIndex;
    public Vector2 position;
    public float   volume;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref frameIndex);
        serializer.SerializeValue(ref position);
        serializer.SerializeValue(ref volume);
    }
}
