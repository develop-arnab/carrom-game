using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Friends;
using Unity.Services.Friends.Models;
using UnityEngine.UI;

public class FriendsListItem : MonoBehaviour
{

    [SerializeField] public TextMeshProUGUI nameText = null;
    [SerializeField] private Button inviteButton = null;
    // [SerializeField] private Button removeButton = null;  // reserved for future use

    private string id = "";
    private string memberId = "";
    private string memberName = "";

    private void Start()
    {
        inviteButton.onClick.AddListener(InviteFriend);
        // removeButton.onClick.AddListener(RemoveFriend);  // reserved for future use
    }

    public void Initialize(Relationship relationship)
    {
        memberId   = relationship.Member.Id;
        memberName = relationship.Member.Profile.Name;
        id         = relationship.Id;
        nameText.text = memberName;
    }

    // ── Pillar A: dispatch a lobby invite message to this friend ─────────────

    private async void InviteFriend()
    {
        inviteButton.interactable = false;
        try
        {
            string lobbyId = LobbyManager.Instance.GetJoinedLobby()?.Id;
            if (string.IsNullOrEmpty(lobbyId))
            {
                ErrorMenu errorPanel = (ErrorMenu)PanelManager.GetSingleton("error");
                errorPanel.Open(ErrorMenu.Action.None, "No active lobby. Please wait a moment and try again.", "OK");
                return;
            }

            string inviterName = AuthenticationService.Instance.PlayerName
                              ?? AuthenticationService.Instance.PlayerId
                              ?? "A friend";

            var message = new LobbyInviteMessage(lobbyId, inviterName);
            await FriendsService.Instance.MessageAsync(memberId, message);
            Debug.Log($"[FriendsListItem] Invite sent to '{memberName}' ({memberId}) for lobby {lobbyId}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FriendsListItem] MessageAsync failed: {e.Message}");
            ErrorMenu errorPanel = (ErrorMenu)PanelManager.GetSingleton("error");
            errorPanel.Open(ErrorMenu.Action.None, "Failed to send invite.", "OK");
        }
        finally
        {
            inviteButton.interactable = true;
        }
    }

    /*  -- reserved for future use --
    private async void RemoveFriend()
    {
        removeButton.interactable = false;
        try
        {
            await FriendsService.Instance.DeleteRelationshipAsync(id);
            Destroy(gameObject);
        }
        catch
        {
            removeButton.interactable = true;
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to remove friend.", "OK");
        }
    }
    */

}