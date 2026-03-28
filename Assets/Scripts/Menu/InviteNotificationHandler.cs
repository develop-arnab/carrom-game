using System.Threading.Tasks;
using TMPro;
using Unity.Services.Lobbies;
using UnityEngine;

/// <summary>
/// Lives on the Bootstrap/LobbyManager GameObject (DontDestroyOnLoad).
/// Receives invite events from LobbyManager and surfaces them in the Menu scene UI.
/// ActionConfirmMenu and Notification_Text are looked up at runtime so they work
/// even though this handler lives in a different scene.
/// </summary>
public class InviteNotificationHandler : MonoBehaviour
{
    private string _pendingLobbyId;

    private void Start()
    {
        // Subscribe in Start() not OnEnable() — LobbyManager.Instance may be null
        // during OnEnable() if both components are on the same GameObject (Awake order).
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnInviteReceived += ShowInvitePopup;
            Debug.Log("[InviteNotificationHandler] Subscribed to LobbyManager.OnInviteReceived.");
        }
        else
        {
            Debug.LogError("[InviteNotificationHandler] LobbyManager.Instance is null in Start() — invite popups will not work.");
        }
    }

    private void OnDisable()
    {
        if (LobbyManager.Instance != null)
            LobbyManager.Instance.OnInviteReceived -= ShowInvitePopup;
    }

    private void ShowInvitePopup(string lobbyId, string inviterName)
    {
        _pendingLobbyId = lobbyId;

        // Strip #NNNN discriminator — show just the base name
        string displayName = inviterName.Contains("#")
            ? inviterName.Substring(0, inviterName.IndexOf('#'))
            : inviterName;

        string message = $"{displayName} Challenged";

        Debug.Log($"[InviteNotificationHandler] ShowInvitePopup — inviter='{displayName}', lobbyId='{lobbyId}'");

        // Write to Notification_Text if it exists in the active scene
        GameObject notifGO = GameObject.Find("Notification_Text");
        if (notifGO != null)
        {
            var tmp = notifGO.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = message;
        }

        // Open the confirm panel — it lives in the Menu scene, looked up at runtime
        ActionConfirmMenu panel = (ActionConfirmMenu)PanelManager.GetSingleton("action_confirm");
        if (panel == null)
        {
            Debug.LogWarning("[InviteNotificationHandler] 'action_confirm' panel not found — player may not be in Menu scene yet.");
            return;
        }

        Debug.Log($"[InviteNotificationHandler] action_confirm panel found — calling Open(). Panel active={panel.gameObject.activeSelf}, container active={panel.IsOpen}");
        panel.Open(OnInviteResponse, message, "Accept", "Decline");
        Debug.Log($"[InviteNotificationHandler] panel.Open() returned. IsOpen={panel.IsOpen}");
    }

    private async void OnInviteResponse(ActionConfirmMenu.Result result)
    {
        // Clear the notification text
        GameObject notifGO = GameObject.Find("Notification_Text");
        if (notifGO != null)
        {
            var tmp = notifGO.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = "";
        }

        if (result != ActionConfirmMenu.Result.Positive)
        {
            _pendingLobbyId = null;
            return;
        }

        string lobbyId = _pendingLobbyId;
        _pendingLobbyId = null;

        try
        {
            await LobbyManager.Instance.JoinLobbyByIdAsync(lobbyId);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[InviteNotificationHandler] JoinLobbyByIdAsync failed: {e.Message}");
            ErrorMenu errorPanel = (ErrorMenu)PanelManager.GetSingleton("error");
            if (errorPanel != null)
                errorPanel.Open(ErrorMenu.Action.None, "Failed to join lobby.", "OK");
        }
    }
}
