using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
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
    
    private void Start()
    {
        inviteButton.onClick.AddListener(InviteFriend);
        // removeButton.onClick.AddListener(RemoveFriend);  // reserved for future use
    }
    
    public void Initialize(Relationship relationship)
    {
        memberId = relationship.Member.Id;
        id = relationship.Id;
        nameText.text = relationship.Member.Profile.Name;
    }
    
    private void InviteFriend()
    {
        inviteButton.interactable = false;
        try
        {
            LobbyManager.Instance.CreateLobby("Carrom", 2, true, LobbyManager.GameMode.Carrom);
        }
        catch
        {
            inviteButton.interactable = true;
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to create lobby.", "OK");
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