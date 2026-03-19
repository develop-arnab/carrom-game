using TMPro;
using UnityEngine;
using Unity.Services.Lobbies.Models;

/// <summary>
/// Attach to a GameObject in the CharacterSelection scene.
/// Reads the current lobby's join code on Start and displays it.
/// Works for both public and private lobbies.
/// </summary>
public class LobbyJoinCodeDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI codeText = null;

    private void Start()
    {
        if (codeText == null)
        {
            Debug.LogWarning("[LobbyJoinCodeDisplay] codeText is not assigned in the Inspector.");
            return;
        }
        string code = LobbyManager.LastLobbyCode;
        codeText.text = code;
        Debug.Log("[LobbyJoinCodeDisplay] Lobby code: " + (string.IsNullOrEmpty(code) ? "none" : code));
    }
}
