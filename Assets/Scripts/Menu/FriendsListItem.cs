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
    [SerializeField] private Button removeButton = null;

    private string id = "";
    private string memberId = "";
    
    private void Start()
    {
        removeButton.onClick.AddListener(RemoveFriend);
    }
    
    public void Initialize(Relationship relationship)
    {
        memberId = relationship.Member.Id;
        id = relationship.Id;
        nameText.text = relationship.Member.Profile.Name;
    }
    
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
    
}