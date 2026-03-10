using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Services.Friends;
using Unity.Services.Friends.Models;
using UnityEngine.UI;

public class FriendRequestReceivedItem : MonoBehaviour
{

    [SerializeField] public TextMeshProUGUI nameText = null;
    [SerializeField] private Button acceptButton = null;
    [SerializeField] private Button rejectButton = null;
    
    private string id = "";
    private string memberId = "";

    private void Start()
    {
        acceptButton.onClick.AddListener(Accept);
        rejectButton.onClick.AddListener(Reject);
    }
    
    public void Initialize(Relationship relationship)
    {
        memberId = relationship.Member.Id;
        id = relationship.Id;
        nameText.text = relationship.Member.Profile.Name;
    }
    
    private async void Accept()
    {
        acceptButton.interactable = false;
        rejectButton.interactable = false;
        try
        {
            await FriendsService.Instance.AddFriendAsync(memberId);
            Destroy(gameObject);
        }
        catch
        {
            acceptButton.interactable = true;
            rejectButton.interactable = true;
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to accept the request.", "OK");
        }
    }
    
    private async void Reject()
    {
        acceptButton.interactable = false;
        rejectButton.interactable = false;
        try
        {
            await FriendsService.Instance.DeleteIncomingFriendRequestAsync(memberId);
            Destroy(gameObject);
        }
        catch
        {
            acceptButton.interactable = true;
            rejectButton.interactable = true;
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to reject the request.", "OK");
        }
    }
    
}