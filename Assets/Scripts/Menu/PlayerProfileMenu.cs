using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Friends;
using Unity.Services.Leaderboards.Models;
using TMPro;
using UnityEngine.UI;

public class PlayerProfileMenu : Panel
{

    [SerializeField] private TextMeshProUGUI nameText = null;
    [SerializeField] private Button closeButton = null;
    [SerializeField] private Button addFriendButton = null;
    [SerializeField] private Button removeFriendButton = null;

    private string _id = null; 
    
    public override void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }
        closeButton.onClick.AddListener(ClosePanel);
        addFriendButton.onClick.AddListener(AddFriend);
        removeFriendButton.onClick.AddListener(RemoveFriend);
        base.Initialize();
    }
    
    public override void Open()
    {
        HideAllButtons();
        base.Open();
        if (string.IsNullOrEmpty(_id))
        {
            _id = AuthenticationService.Instance.PlayerId;
        }
        nameText.text = "";
    }

    public void Open(string playerId, string playerName)
    {
        _id = playerId;
        Open();
        SetupUI(playerId, playerName);
    }
    
    private void SetupUI(string id, string playerName)
    {
        nameText.text = playerName;
        if (id != AuthenticationService.Instance.PlayerId)
        {
            bool isFriend = IsFriend(id);
            addFriendButton.gameObject.SetActive(isFriend == false);
            addFriendButton.interactable = IsSentFriendRequest(id) == false;
            removeFriendButton.gameObject.SetActive(isFriend);
        }
    }
    
    private async void AddFriend()
    {
        addFriendButton.interactable = false;
        try
        {
            await FriendsService.Instance.AddFriendAsync(_id);
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Friend request sent successfully.", "OK");
        }
        catch (Exception exception)
        {
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to send the friend request.", "OK");
            Debug.Log(exception.Message);
            addFriendButton.interactable = true;
        }
    }

    private async void RemoveFriend()
    {
        removeFriendButton.interactable = false;
        try
        {
            await FriendsService.Instance.DeleteFriendAsync(_id);
            addFriendButton.gameObject.SetActive(true);
            removeFriendButton.gameObject.SetActive(false);
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Player removed from your friends list successfully.", "OK");
        }
        catch
        {
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to remove the player from your friends list.", "OK");
        }
        removeFriendButton.interactable = true;
    }
    
    private bool IsFriend(string id)
    {
        for (int i = 0; i < FriendsService.Instance.Friends.Count; i++)
        {
            if (FriendsService.Instance.Friends[i].Member.Id == id)
            {
                return true;
            }
        }
        return false;
    }
    
    private bool IsSentFriendRequest(string id)
    {
        for (int i = 0; i < FriendsService.Instance.OutgoingFriendRequests.Count; i++)
        {
            if (FriendsService.Instance.OutgoingFriendRequests[i].Member.Id == id)
            {
                return true;
            }
        }
        return false;
    }
    
    private void ClosePanel()
    {
        Close();
        _id = null;
    }
    
    private void HideAllButtons()
    {
        addFriendButton.gameObject.SetActive(false);
        removeFriendButton.gameObject.SetActive(false);
        removeFriendButton.interactable = true;
        addFriendButton.interactable = true;
    }

}