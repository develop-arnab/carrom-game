using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Manages deterministic bidirectional mapping between piece IDs and GameObjects.
/// CRITICAL: Uses sorted positions to guarantee identical ID assignment on both clients.
/// Piece ID Assignment:
/// - Striker: ID 0  (tag "Striker")
/// - White coins: IDs 1-9  (tag "White", sorted deterministically)
/// - Black coins: IDs 10-18 (tag "Black", sorted deterministically)
/// - Queen: ID 19 (tag "Queen")
/// </summary>
public class PieceRegistry : MonoBehaviour
{
    private Dictionary<byte, GameObject> idToPiece = new Dictionary<byte, GameObject>();
    private Dictionary<GameObject, byte> pieceToId = new Dictionary<GameObject, byte>();

    private void Start()
    {
        RegisterPiecesByTag("Striker",  0, 1);   // ID 0
        RegisterPiecesByTag("White",    1, 9);   // IDs 1-9
        RegisterPiecesByTag("Black",   10, 9);   // IDs 10-18
        RegisterPiecesByTag("Queen",   19, 1);   // ID 19
        Debug.Log($"[PieceRegistry] Registered {Count} pieces total");
    }

    /// <summary>
    /// Finds all GameObjects with the given tag, sorts them deterministically,
    /// then assigns sequential IDs starting at startId.
    /// Sort order: NetworkObjectId (if present) → position.x → position.y
    /// This guarantees both Host and Client assign the same ID to the same coin.
    /// </summary>
    private void RegisterPiecesByTag(string tag, byte startId, int expectedCount)
    {
        GameObject[] pieces = GameObject.FindGameObjectsWithTag(tag);

        if (pieces.Length == 0)
        {
            Debug.LogWarning($"[PieceRegistry] No pieces found with tag '{tag}'");
            return;
        }

        if (pieces.Length != expectedCount)
        {
            Debug.LogWarning($"[PieceRegistry] Expected {expectedCount} pieces with tag '{tag}', found {pieces.Length}");
        }

        // Sort deterministically so both clients assign the same IDs
        GameObject[] sorted = pieces.OrderBy(go =>
        {
            NetworkObject no = go.GetComponent<NetworkObject>();
            return no != null ? (long)no.NetworkObjectId : long.MaxValue;
        })
        .ThenBy(go => go.transform.position.x)
        .ThenBy(go => go.transform.position.y)
        .ToArray();

        for (int i = 0; i < sorted.Length; i++)
        {
            byte id = (byte)(startId + i);
            RegisterPiece(id, sorted[i]);
            Debug.Log($"[PieceRegistry] ID {id} → {sorted[i].name} (pos: {sorted[i].transform.position.x:F2}, {sorted[i].transform.position.y:F2})");
        }
    }

    public void RegisterPiece(byte id, GameObject piece)
    {
        if (piece == null) { Debug.LogError($"Cannot register null piece with ID {id}"); return; }
        if (idToPiece.ContainsKey(id)) pieceToId.Remove(idToPiece[id]);
        if (pieceToId.ContainsKey(piece)) idToPiece.Remove(pieceToId[piece]);
        idToPiece[id] = piece;
        pieceToId[piece] = id;
    }

    public GameObject GetPiece(byte id)
    {
        idToPiece.TryGetValue(id, out GameObject piece);
        return piece;
    }

    public byte GetId(GameObject piece)
    {
        if (pieceToId.TryGetValue(piece, out byte id)) return id;
        return 255;
    }

    public bool HasPiece(byte id) => idToPiece.ContainsKey(id);
    public void Clear() { idToPiece.Clear(); pieceToId.Clear(); }
    public int Count => idToPiece.Count;
}
