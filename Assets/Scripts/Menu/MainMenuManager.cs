using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Services.Authentication;
using UnityEngine.UI;
using Unity.Services.Friends;
using UnityEngine.SceneManagement;

public class MainMenu : Panel
{

    [SerializeField] public TextMeshProUGUI nameText = null;
    [SerializeField] private Button logoutButton = null;
    [SerializeField] private Button leaderboardsButton = null;
    [SerializeField] private Button friendsButton = null;
    [SerializeField] private Button renameButton = null;
    [SerializeField] private Button customizationButton = null;
    [SerializeField] private Button playBetsButton = null;
    [SerializeField] private Button playCarromButton = null;
    [SerializeField] private TMP_InputField joinCodeInput = null;
    [SerializeField] private Button joinButton = null;

    private bool isFriendsServiceInitialized = false;
    
    public override void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }
        logoutButton.onClick.AddListener(SignOut);
        leaderboardsButton.onClick.AddListener(Leaderboards);
        friendsButton.onClick.AddListener(Friends);
        renameButton.onClick.AddListener(RenamePlayer);
        customizationButton.onClick.AddListener(Customization);
        playBetsButton.onClick.AddListener(PlayBets);
        if (playCarromButton != null) playCarromButton.onClick.AddListener(PlayCarrom);
        if (joinButton != null) joinButton.onClick.AddListener(JoinByCode);
        base.Initialize();
    }
    
    public override void Open()
    {
        friendsButton.interactable = isFriendsServiceInitialized;
        UpdatePlayerNameUI();
        if (isFriendsServiceInitialized == false)
        {
            InitializeFriendsServiceAsync();
        }
        base.Open();
    }
    
    private void Customization()
    {
        PanelManager.Open("customization");
    }

    private void PlayBets()
    {
        SceneManager.LoadScene(4);
    }

    private void PlayCarrom()
    {
        if (playCarromButton != null) playCarromButton.interactable = false;
        LobbyManager.Instance.QuickJoinOrCreatePublicLobby();
    }

    private void JoinByCode()
    {
        if (joinCodeInput == null || joinButton == null) return;
        string code = joinCodeInput.text.Trim();
        if (string.IsNullOrEmpty(code)) return;

        joinButton.interactable = false;
        joinCodeInput.interactable = false;
        try
        {
            LobbyManager.Instance.JoinLobbyByCode(code);
        }
        catch
        {
            joinButton.interactable = true;
            joinCodeInput.interactable = true;
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to join lobby.", "OK");
        }
    }

    private async void InitializeFriendsServiceAsync()
    {
        try
        {
            await FriendsService.Instance.InitializeAsync();
            isFriendsServiceInitialized = true;
            friendsButton.interactable = true;
        }
        catch (Exception exception)
        {
            Debug.Log(exception.Message);
        }
    }
    
    private void SignOut()
    {
        ActionConfirmMenu panel = (ActionConfirmMenu)PanelManager.GetSingleton("action_confirm");
        panel.Open(SignOutResult, "Quit?", "Exit", "Play");
    }
    
    private void SignOutResult(ActionConfirmMenu.Result result)
    {
        if (result == ActionConfirmMenu.Result.Positive)
        {
            MainMenuManager.Singleton.SignOut();
            isFriendsServiceInitialized = false;
        }
    }
    
    public void UpdatePlayerNameUI()
    {
        string fullName = AuthenticationService.Instance.PlayerName ?? "";
        // Show full name including #NNNN so players can share it for friend requests
        nameText.text = fullName;
    }
    
    private void Leaderboards()
    {
        PanelManager.Open("leaderboards");
    }
    
    private void Friends()
    {
        PanelManager.Open("friends");
    }
    
    private void RenamePlayer()
    {
        GetInputMenu panel = (GetInputMenu)PanelManager.GetSingleton("input");
        panel.Open(RenamePlayerConfirm, GetInputMenu.Type.String, 20, "Enter a name for your account.", "Send", "Cancel");
    }
    
    private async void RenamePlayerConfirm(string input)
    {
        renameButton.interactable = false;
        try
        {
            await AuthenticationService.Instance.UpdatePlayerNameAsync(input);
            UpdatePlayerNameUI();
        }
        catch
        {
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to change the account name.", "OK");
        }
        renameButton.interactable = true;
    }
    
}