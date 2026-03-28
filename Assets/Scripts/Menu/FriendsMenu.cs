using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Services.Authentication;
using Unity.Services.Friends;
using Unity.Services.Friends.Notifications;

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
    [SerializeField] private Button addFriendButton = null;
    [SerializeField] private TMP_InputField addFriendInput = null;

    // Which tab is currently showing — so push events can refresh the right view
    private enum Tab { Friends, Received, Sent }
    private Tab currentTab = Tab.Friends;

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
        if (addFriendButton != null) addFriendButton.onClick.AddListener(AddFriendByName);
        ClearFriendsList();
        base.Initialize();
    }

    public override void Open()
    {
        base.Open();
        // Show the panel immediately — don't block on FriendsService state.
        // Subscribe and load list if service is ready; otherwise do it async.
        if (IsFriendsServiceReady())
        {
            FriendsService.Instance.RelationshipAdded   += OnRelationshipChanged;
            FriendsService.Instance.RelationshipDeleted += OnRelationshipChanged;
            LoadFriendsList();
        }
        else
        {
            Debug.Log("[FriendsMenu] FriendsService not ready on Open — initializing async.");
            InitAndOpenAsync();
        }
    }

    private bool IsFriendsServiceReady()
    {
        try
        {
            // Accessing Friends triggers ValidateInitialized — if it throws, service isn't ready
            _ = FriendsService.Instance.Friends;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async void InitAndOpenAsync()
    {
        try
        {
            await FriendsService.Instance.InitializeAsync();
            // Only subscribe and load if the panel is still open after the await
            if (IsOpen)
            {
                FriendsService.Instance.RelationshipAdded   += OnRelationshipChanged;
                FriendsService.Instance.RelationshipDeleted += OnRelationshipChanged;
                LoadFriendsList();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FriendsMenu] InitAndOpenAsync failed: {e.Message}");
        }
    }

    public override void Close()
    {
        // Guard against Close() being called during Panel.Initialize() before Friends is ready
        try
        {
            FriendsService.Instance.RelationshipAdded   -= OnRelationshipChanged;
            FriendsService.Instance.RelationshipDeleted -= OnRelationshipChanged;
        }
        catch { }
        base.Close();
    }

    // Push notification handler — refreshes whichever tab is open
    private void OnRelationshipChanged(IRelationshipAddedEvent e)   => RefreshCurrentTab();
    private void OnRelationshipChanged(IRelationshipDeletedEvent e) => RefreshCurrentTab();

    private void RefreshCurrentTab()
    {
        switch (currentTab)
        {
            case Tab.Friends:  LoadFriendsList();             break;
            case Tab.Received: LoadReceivedFriendRequests();  break;
            case Tab.Sent:     LoadSentFriendRequests();      break;
        }
    }

    private async void AddFriendByName()
    {
        if (addFriendInput == null || string.IsNullOrWhiteSpace(addFriendInput.text)) return;

        string name = addFriendInput.text.Trim();
        addFriendButton.interactable = false;
        try
        {
            await FriendsService.Instance.AddFriendByNameAsync(name);
            addFriendInput.text = "";
            // Refresh sent requests tab to show the new outgoing request
            LoadSentFriendRequests();
        }
        catch
        {
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Could not find a player with that name.", "OK");
        }
        addFriendButton.interactable = true;
    }
    
    private void LoadFriendsList()
    {
        currentTab = Tab.Friends;
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
        currentTab = Tab.Received;
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
        currentTab = Tab.Sent;
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