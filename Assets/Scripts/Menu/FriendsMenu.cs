using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Services.Authentication;
using Unity.Services.Friends;

public class FriendsMenu : Panel
{

    [SerializeField] private FriendsListItem friendsListItemPrefab = null;
    [SerializeField] private FriendRequestReceivedItem friendRequestReceivedItemPrefab = null;
    [SerializeField] private FriendRequestSentItem friendRequestSentItemPrefab = null;
    [SerializeField] private RectTransform friendsListContainer = null;
    [SerializeField] private Button friendsButton = null;
    [SerializeField] private Button friendRequestsReceivedButton = null;
    [SerializeField] private Button friendRequestsSentButton = null;
    [SerializeField] private Button closeButton = null;

    public override void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }
        friendsButton.onClick.AddListener(LoadFriendsList);
        friendRequestsReceivedButton.onClick.AddListener(LoadReceivedFriendRequests);
        friendRequestsSentButton.onClick.AddListener(LoadSentFriendRequests);
        closeButton.onClick.AddListener(ClosePanel);
        ClearFriendsList();
        base.Initialize();
    }
    
    public override void Open()
    {
        base.Open();
        LoadFriendsList();
    }
    
    private void LoadFriendsList()
    {
        friendsButton.interactable = false;
        friendRequestsReceivedButton.interactable = true;
        friendRequestsSentButton.interactable = true;
        if (FriendsService.Instance.Friends != null)
        {
            ClearFriendsList();
            for (int i = 0; i < FriendsService.Instance.Friends.Count; i++)
            {
                FriendsListItem item = Instantiate(friendsListItemPrefab, friendsListContainer);
                item.Initialize(FriendsService.Instance.Friends[i]);
            }
        }
    }

    private void LoadReceivedFriendRequests()
    {
        friendsButton.interactable = true;
        friendRequestsReceivedButton.interactable = false;
        friendRequestsSentButton.interactable = true;
        ClearFriendsList();
        if (FriendsService.Instance.IncomingFriendRequests != null)
        {
            for (int i = 0; i < FriendsService.Instance.IncomingFriendRequests.Count; i++)
            {
                FriendRequestReceivedItem receivedItem = Instantiate(friendRequestReceivedItemPrefab, friendsListContainer);
                receivedItem.Initialize(FriendsService.Instance.IncomingFriendRequests[i]);
            }
        }
    }

    private void LoadSentFriendRequests()
    {
        friendsButton.interactable = true;
        friendRequestsReceivedButton.interactable = true;
        friendRequestsSentButton.interactable = false;
        ClearFriendsList();
        if (FriendsService.Instance.OutgoingFriendRequests != null)
        {
            for (int i = 0; i < FriendsService.Instance.OutgoingFriendRequests.Count; i++)
            {
                FriendRequestSentItem receivedItem = Instantiate(friendRequestSentItemPrefab, friendsListContainer);
                receivedItem.Initialize(FriendsService.Instance.OutgoingFriendRequests[i]);
            }
        }
    }

    private void ClosePanel()
    {
        Close();
    }
    
    private void ClearFriendsList()
    {
        FriendsListItem[] items = friendsListContainer.GetComponentsInChildren<FriendsListItem>();
        if (items != null)
        {
            for (int i = 0; i < items.Length; i++)
            {
                Destroy(items[i].gameObject);
            }
        }
        FriendRequestReceivedItem[] received = friendsListContainer.GetComponentsInChildren<FriendRequestReceivedItem>();
        if (received != null)
        {
            for (int i = 0; i < received.Length; i++)
            {
                Destroy(received[i].gameObject);
            }
        }
        FriendRequestSentItem[] sent = friendsListContainer.GetComponentsInChildren<FriendRequestSentItem>();
        if (sent != null)
        {
            for (int i = 0; i < sent.Length; i++)
            {
                Destroy(sent[i].gameObject);
            }
        }
    }
    
}