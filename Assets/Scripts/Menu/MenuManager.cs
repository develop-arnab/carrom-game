using System;
/*using System.Collections;
using System.Collections.Generic;*/
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Core;

public class MainMenuManager : MonoBehaviour
{
    
    private bool initialized = false;
    private bool eventsInitialized = false;
    
    private static MainMenuManager singleton = null;

    public static MainMenuManager Singleton
    {
        get
        {
            if (singleton == null)
            {
                singleton = FindFirstObjectByType<MainMenuManager>();
                singleton.Initialize();
            }
            return singleton; 
        }
    }

    private void Initialize()
    {
        if (initialized) { return; }
        initialized = true;
    }
    
    private void OnDestroy()
    {
        if (singleton == this)
        {
            singleton = null;
        }
    }

    private void Awake()
    {
        Application.runInBackground = true;
        StartClientService();
    }

    public async void StartClientService()
    {
        PanelManager.CloseAll();
        PanelManager.Open("loading");
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                options.SetProfile("default_profile");
                await UnityServices.InitializeAsync();
            }
            
            if (!eventsInitialized)
            {
                SetupEvents();
            }

            if (AuthenticationService.Instance.SessionTokenExists)
            {
                // Resume the existing session (works for both username/password and anonymous)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            else
            {
                PanelManager.Open("auth");
            }
        }
        catch (Exception exception)
        {
            ShowError(ErrorMenu.Action.StartService, "Failed to connect to the network.", "Retry");
        }
    }

    public async void SignInAnonymouslyAsync()
    {
        PanelManager.Open("loading");
        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        catch (AuthenticationException exception)
        {
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to sign in.", "OK");
        }
        catch (RequestFailedException exception)
        {
            ShowError(ErrorMenu.Action.SignIn, "Failed to connect to the network.", "Retry");
        }
    }
    
    public async void SignInWithUsernameAndPasswordAsync(string username, string password)
    {
        PanelManager.Open("loading");
        try
        {
            bool isNewAccount = false;
            try
            {
                // Try sign-up first — if account already exists, fall through to sign-in
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                isNewAccount = true;
            }
            catch (RequestFailedException)
            {
                // Sign-up failed (likely username taken) — attempt sign-in
                try
                {
                    await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
                }
                catch (RequestFailedException signInEx) when (signInEx.Message.Contains("WRONG_USERNAME_PASSWORD"))
                {
                    PanelManager.Close("loading");
                    PanelManager.Open("auth");
                    ((AuthenticationMenu)PanelManager.GetSingleton("auth")).ShowError("Incorrect username or password.");
                    return;
                }
            }

            if (isNewAccount)
            {
                // Set display name to match the chosen username
                await AuthenticationService.Instance.UpdatePlayerNameAsync(username);
            }

            PanelManager.CloseAll();
            PanelManager.Open("main");
            ((MainMenu)PanelManager.GetSingleton("main")).UpdatePlayerNameUI();
        }
        catch (Exception)
        {
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to connect to the network.", "OK");
        }
    }

    public void SignUpWithUsernameAndPasswordAsync(string username, string password)
        => SignInWithUsernameAndPasswordAsync(username, password);
    
    public void SignOut()
    {
        AuthenticationService.Instance.SignOut();
        PanelManager.CloseAll();
        PanelManager.Open("auth");
    }
    
    private void SetupEvents()
    {
        eventsInitialized = true;
        AuthenticationService.Instance.SignedIn += () =>
        {
            SignInConfirmAsync();
        };

        AuthenticationService.Instance.SignedOut += () =>
        {
            PanelManager.CloseAll();
            PanelManager.Open("auth");
        };
        
        AuthenticationService.Instance.Expired += () =>
        {
            PanelManager.CloseAll();
            PanelManager.Open("auth");
        };
    }
    
    private void ShowError(ErrorMenu.Action action = ErrorMenu.Action.None, string error = "", string button = "")
    {
        PanelManager.Close("loading");
        ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
        panel.Open(action, error, button);
    }
    
    private void SignInConfirmAsync()
    {
        // Used for session token resume — open main panel and refresh name
        PanelManager.CloseAll();
        PanelManager.Open("main");
        ((MainMenu)PanelManager.GetSingleton("main")).UpdatePlayerNameUI();
    }
    
}