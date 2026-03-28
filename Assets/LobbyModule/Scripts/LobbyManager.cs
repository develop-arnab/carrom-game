using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Friends;
using Unity.Services.Friends.Notifications;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Message payload sent via FriendsService.MessageAsync to invite a friend to a lobby.
/// Must have an empty constructor and be JSON-serializable (required by IMessagingService).
/// </summary>
[Serializable]
public class LobbyInviteMessage
{
    public string LobbyId;
    public string InviterName;
    public LobbyInviteMessage() { }
    public LobbyInviteMessage(string lobbyId, string inviterName)
    {
        LobbyId     = lobbyId;
        InviterName = inviterName;
    }
}

public class LobbyManager : MonoBehaviour {


    public static LobbyManager Instance { get; private set; }


    public static bool IsHost { get; private set; }
    public static string RelayJoinCode { get; private set; }



    public const string KEY_PLAYER_NAME = "PlayerName";
    public const string KEY_PLAYER_CHARACTER = "Character";
    public const string KEY_GAME_MODE = "GameMode";
    public const string KEY_START_GAME = "StartGame";
    public const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";



    public event EventHandler OnLeftLobby;

    public event EventHandler<LobbyEventArgs> OnJoinedLobby;
    public event EventHandler<LobbyEventArgs> OnJoinedLobbyUpdate;
    public event EventHandler<LobbyEventArgs> OnKickedFromLobby;
    public event EventHandler<LobbyEventArgs> OnLobbyGameModeChanged;
    public event EventHandler<LobbyEventArgs> OnLobbyStartGame;
    public class LobbyEventArgs : EventArgs {
        public Lobby lobby;
    }

    public event EventHandler<OnLobbyListChangedEventArgs> OnLobbyListChanged;
    public class OnLobbyListChangedEventArgs : EventArgs {
        public List<Lobby> lobbyList;
    }

    // ── Phase 2: Real-Time Invite System ─────────────────────────────────────

    /// <summary>
    /// Raised on the main thread when a lobby invite message is received via FriendsService.
    /// Payload: (lobbyId, inviterName)
    /// </summary>
    public event Action<string, string> OnInviteReceived;

    private string _pendingInviteLobbyId;
    private string _pendingInviterName;


    public enum GameMode {
        TicTacToe,
        RollDice,
        Carrom
    }

    public enum PlayerCharacter {
        Marine,
        Ninja,
        Zombie
    }



    private float heartbeatTimer;
    private float lobbyPollTimer;
    private float refreshLobbyListTimer = 5f;
    private Lobby joinedLobby;
    private string playerName;
    private bool alreadyStartedGame;
    [SerializeField]
    private SceneName nextScene = SceneName.CharacterSelection;
    private void Awake() {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Persists the lobby join code across scene loads so UI in CharacterSelection can display it
    public static string LastLobbyCode { get; private set; } = "";

    private void Start() {
        // UGS may not be initialized yet when LobbyManager.Start() runs.
        // Start a coroutine that waits for UGS to be ready, then hooks into auth.
        StartCoroutine(WaitForUgsAndSetupFriends());
    }

    private System.Collections.IEnumerator WaitForUgsAndSetupFriends()
    {
        // Wait until UnityServices is initialized
        yield return new WaitUntil(() =>
            Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized);

        // Subscribe to future sign-in events (covers the case where sign-in hasn't happened yet)
        AuthenticationService.Instance.SignedIn += OnSignedIn;

        // If already signed in (e.g. returning from another scene), initialize immediately
        if (AuthenticationService.Instance.IsSignedIn)
        {
            playerName = AuthenticationService.Instance.PlayerName ?? AuthenticationService.Instance.PlayerId;
            Debug.Log("[LobbyManager] Already signed in — initializing Friends. Player: " + playerName);
            _ = InitializeFriendsAndListenAsync();
        }
    }

    private void OnDestroy()
    {
        if (Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized)
        {
            try { AuthenticationService.Instance.SignedIn -= OnSignedIn; } catch { }
        }
    }

    private void OnSignedIn()
    {
        playerName = AuthenticationService.Instance.PlayerName ?? AuthenticationService.Instance.PlayerId;
        Debug.Log("[LobbyManager] SignedIn event — initializing Friends service. Player: " + playerName);
        _ = InitializeFriendsAndListenAsync();
    }

    private void Update() {
        //HandleRefreshLobbyList(); // Disabled Auto Refresh for testing with multiple builds
        HandleLobbyHeartbeat();
        HandleLobbyPolling();
    }

    public async void Authenticate(string playerName) {
        Debug.Log("Authenticate Called " + playerName);
        playerName = playerName.Replace(" ", "_");
        this.playerName = playerName;
        InitializationOptions initializationOptions = new InitializationOptions();
        initializationOptions.SetProfile(playerName);

        await UnityServices.InitializeAsync(initializationOptions);

        AuthenticationService.Instance.SignedIn += () => {
            // do nothing
            Debug.Log("Signed in! " + AuthenticationService.Instance.PlayerId);

            RefreshLobbyList();
        };

        await AuthenticationService.Instance.SignInAnonymouslyAsync();
        _ = InitializeFriendsAndListenAsync();
    }

    private void HandleRefreshLobbyList() {
        if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn) {
            refreshLobbyListTimer -= Time.deltaTime;
            if (refreshLobbyListTimer < 0f) {
                float refreshLobbyListTimerMax = 5f;
                refreshLobbyListTimer = refreshLobbyListTimerMax;

                RefreshLobbyList();
            }
        }
    }

    private async void HandleLobbyHeartbeat() {
        if (IsLobbyHost()) {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer < 0f) {
                float heartbeatTimerMax = 15f;
                heartbeatTimer = heartbeatTimerMax;

                Debug.Log("Heartbeat");
                await LobbyService.Instance.SendHeartbeatPingAsync(joinedLobby.Id);
            }
        }
    }

    private async void HandleLobbyPolling() {
        if (joinedLobby != null) {
            lobbyPollTimer -= Time.deltaTime;
            if (lobbyPollTimer < 0f) {
                float lobbyPollTimerMax = 1.1f;
                lobbyPollTimer = lobbyPollTimerMax;

                joinedLobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);

                OnJoinedLobbyUpdate?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });

                if (!IsLobbyHost()) {
                    string relayCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
                    if (!string.IsNullOrEmpty(relayCode) && !alreadyStartedGame) {
                        // Client found the relay code — join the NGO session in the waiting room.
                        // Do NOT fire OnLobbyStartGame here; that would trigger a scene load.
                        alreadyStartedGame = true;
                        RelayJoinCode = relayCode;
                        Debug.Log($"[LobbyManager] Client found relay code — joining NGO session: {relayCode}");
                        await StartGameManager.Instance.JoinRelayAsync(relayCode);
                    }
                }

                if (!IsPlayerInLobby()) {
                    // Player was kicked out of this lobby
                    Debug.Log("Kicked from Lobby!");

                    OnKickedFromLobby?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });

                    joinedLobby = null;
                }
            }
        }
    }

    public Lobby GetJoinedLobby() {
        return joinedLobby;
    }

    public bool IsLobbyHost() {
        return joinedLobby != null && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }

    private bool IsPlayerInLobby() {
        if (joinedLobby != null && joinedLobby.Players != null) {
            foreach (Player player in joinedLobby.Players) {
                if (player.Id == AuthenticationService.Instance.PlayerId) {
                    // This player is in this lobby
                    return true;
                }
            }
        }
        return false;
    }

    private Player GetPlayer() {
        return new Player(AuthenticationService.Instance.PlayerId, null, new Dictionary<string, PlayerDataObject> {
            { KEY_PLAYER_NAME, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName) },
            { KEY_PLAYER_CHARACTER, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, PlayerCharacter.Marine.ToString()) }
        });
    }

    public void ChangeGameMode() {
        if (IsLobbyHost()) {
            GameMode gameMode =
                Enum.Parse<GameMode>(joinedLobby.Data[KEY_GAME_MODE].Value);

            switch (gameMode) {
                default:
                case GameMode.TicTacToe:
                    gameMode = GameMode.RollDice;
                    break;
                case GameMode.RollDice:
                    gameMode = GameMode.TicTacToe;
                    break;
            }

            UpdateLobbyGameMode(gameMode);
        }
    }

    /// <param name="isHostLobby">
    /// When true, suppresses auto-navigation: OnLobbyStartGame is NOT fired and
    /// IsHost/alreadyStartedGame are NOT set. The host stays in the lobby scene
    /// waiting for players. LastLobbyCode is always set regardless of this flag.
    /// </param>
    public async void CreateLobby(string lobbyName, int maxPlayers, bool isPrivate, GameMode gameMode, bool isHostLobby = false) {

        Debug.Log("Creating LOBBY ");
        Player player = GetPlayer();
        Debug.Log("Creating LOBBY PLAYER ");
        CreateLobbyOptions options = new CreateLobbyOptions {
            Player = player,
            IsPrivate = isPrivate,
            Data = new Dictionary<string, DataObject> {
                { KEY_GAME_MODE, new DataObject(DataObject.VisibilityOptions.Public, gameMode.ToString()) },
                { KEY_RELAY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, "") }
            }
        };

        Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);

        joinedLobby = lobby;
        LastLobbyCode = lobby.LobbyCode; // always persist — UI reads this after spawn

        OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = lobby });

        Debug.Log("Created Lobby " + lobby.Name + " with Code " + lobby.LobbyCode);

        if (!isHostLobby)
        {
            // Legacy path: auto-navigate immediately (used by old 1v1 flow)
            IsHost = true;
            alreadyStartedGame = true;
            OnLobbyStartGame?.Invoke(this, new LobbyEventArgs { lobby = lobby });
        }
        // isHostLobby == true: host stays in lobby scene, waits for players,
        // and triggers game start explicitly via StartGameLobbyManager.OnStartGameClicked()
    }

    public async void RefreshLobbyList() {
        try {
            QueryLobbiesOptions options = new QueryLobbiesOptions();
            options.Count = 25;

            // Filter for open lobbies only
            options.Filters = new List<QueryFilter> {
                new QueryFilter(
                    field: QueryFilter.FieldOptions.AvailableSlots,
                    op: QueryFilter.OpOptions.GT,
                    value: "0")
            };

            // Order by newest lobbies first
            options.Order = new List<QueryOrder> {
                new QueryOrder(
                    asc: false,
                    field: QueryOrder.FieldOptions.Created)
            };

            QueryResponse lobbyListQueryResponse = await Lobbies.Instance.QueryLobbiesAsync();

            OnLobbyListChanged?.Invoke(this, new OnLobbyListChangedEventArgs { lobbyList = lobbyListQueryResponse.Results });
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    public async void JoinLobbyByCode(string lobbyCode) {
        Player player = GetPlayer();
        try {
            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, new JoinLobbyByCodeOptions {
                Player = player
            });
            joinedLobby = lobby;
            LastLobbyCode = lobby.LobbyCode;
            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = lobby });
        } catch (LobbyServiceException e) {
            Debug.Log(e);
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to join lobby. Check the code and try again.", "OK");
        }
    }

    public async void JoinLobby(Lobby lobby) {
        Player player = GetPlayer();

        joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id, new JoinLobbyByIdOptions {
            Player = player
        });

        OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = lobby });
    }
    

    public async void UpdatePlayerName(string playerName) {
        this.playerName = playerName;

        if (joinedLobby != null) {
            try {
                UpdatePlayerOptions options = new UpdatePlayerOptions();

                options.Data = new Dictionary<string, PlayerDataObject>() {
                    {
                        KEY_PLAYER_NAME, new PlayerDataObject(
                            visibility: PlayerDataObject.VisibilityOptions.Public,
                            value: playerName)
                    }
                };

                string playerId = AuthenticationService.Instance.PlayerId;

                Lobby lobby = await LobbyService.Instance.UpdatePlayerAsync(joinedLobby.Id, playerId, options);
                joinedLobby = lobby;

                OnJoinedLobbyUpdate?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
            } catch (LobbyServiceException e) {
                Debug.Log(e);
            }
        }
    }

    public async void UpdatePlayerCharacter(PlayerCharacter playerCharacter) {
        if (joinedLobby != null) {
            try {
                UpdatePlayerOptions options = new UpdatePlayerOptions();

                options.Data = new Dictionary<string, PlayerDataObject>() {
                    {
                        KEY_PLAYER_CHARACTER, new PlayerDataObject(
                            visibility: PlayerDataObject.VisibilityOptions.Public,
                            value: playerCharacter.ToString())
                    }
                };

                string playerId = AuthenticationService.Instance.PlayerId;

                Lobby lobby = await LobbyService.Instance.UpdatePlayerAsync(joinedLobby.Id, playerId, options);
                joinedLobby = lobby;

                OnJoinedLobbyUpdate?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
            } catch (LobbyServiceException e) {
                Debug.Log(e);
            }
        }
    }

    public async void QuickJoinLobby() {
        try {
            QuickJoinLobbyOptions options = new QuickJoinLobbyOptions();

            Lobby lobby = await LobbyService.Instance.QuickJoinLobbyAsync(options);
            joinedLobby = lobby;

            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = lobby });
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    /// <summary>
    /// Tries to quick-join an existing public Carrom lobby.
    /// If none is available, creates a new public lobby and becomes host.
    /// Returns true if this player is the host (created the lobby),
    /// false if they joined an existing one as a client.
    /// The caller is responsible for calling CreateRelayAsync() or JoinRelayAsync() after.
    /// </summary>
    public async Task<bool> QuickJoinOrCreatePublicLobbyAsync() {
        try {
            QuickJoinLobbyOptions quickJoinOptions = new QuickJoinLobbyOptions {
                Filter = new List<QueryFilter> {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                },
                Player = GetPlayer()
            };

            Lobby lobby = await LobbyService.Instance.QuickJoinLobbyAsync(quickJoinOptions);
            joinedLobby = lobby;
            LastLobbyCode = lobby.LobbyCode ?? "";
            IsHost = false; // joined existing — we are a client
            Debug.Log("[LobbyManager] Quick-joined existing lobby: " + lobby.Name);
            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = lobby });
            return false; // client
        } catch (LobbyServiceException) {
            // No open lobby found — create a new public one and become host
            Debug.Log("[LobbyManager] No open lobby found — creating new public Carrom lobby");
            int playerCount = StartGameLobbyManager.LocalPlayerCount;
            await CreateLobbyAsync("Carrom", playerCount, false, GameMode.Carrom);
            return true; // host
        }
    }

    /// <summary>
    /// Async-awaitable lobby creation. Always isHostLobby = true (no auto-navigation).
    /// </summary>
    public async Task CreateLobbyAsync(string lobbyName, int maxPlayers, bool isPrivate, GameMode gameMode) {
        Player player = GetPlayer();
        CreateLobbyOptions options = new CreateLobbyOptions {
            Player = player,
            IsPrivate = isPrivate,
            Data = new Dictionary<string, DataObject> {
                { KEY_GAME_MODE, new DataObject(DataObject.VisibilityOptions.Public, gameMode.ToString()) },
                { KEY_RELAY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, "") }
            }
        };
        Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
        joinedLobby = lobby;
        LastLobbyCode = lobby.LobbyCode;
        IsHost = true;
        Debug.Log($"[LobbyManager] Created lobby: {lobby.Name} ({lobby.LobbyCode})");
        OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = lobby });
    }

    public async void LeaveLobby() {
        if (joinedLobby != null) {
            try {
                await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);

                joinedLobby = null;

                OnLeftLobby?.Invoke(this, EventArgs.Empty);
            } catch (LobbyServiceException e) {
                Debug.Log(e);
            }
        }
    }

    public async void KickPlayer(string playerId) {
        if (IsLobbyHost()) {
            try {
                await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, playerId);
            } catch (LobbyServiceException e) {
                Debug.Log(e);
            }
        }
    }

    public async void UpdateLobbyGameMode(GameMode gameMode) {
        try {
            Debug.Log("UpdateLobbyGameMode " + gameMode);
            
            Lobby lobby = await Lobbies.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions {
                Data = new Dictionary<string, DataObject> {
                    { KEY_GAME_MODE, new DataObject(DataObject.VisibilityOptions.Public, gameMode.ToString()) }
                }
            });

            joinedLobby = lobby;

            OnLobbyGameModeChanged?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    public async void StartGame() {
        try {
            Debug.Log("StartGame");

            Lobby lobby = await Lobbies.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions {
                Data = new Dictionary<string, DataObject> {
                    { KEY_START_GAME, new DataObject(DataObject.VisibilityOptions.Public, "1") }
                }
            });

            joinedLobby = lobby;

            IsHost = true;
            alreadyStartedGame = true;
            // SceneManager.LoadScene(5);
            LoadingSceneManager.Instance.LoadScene(nextScene);
            OnLobbyStartGame?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    private void JoinGame(string relayJoinCode) {
        // Deprecated — client relay joining is now handled directly in HandleLobbyPolling
        // via StartGameManager.Instance.JoinRelayAsync(). This method is kept as a stub
        // to avoid breaking any external callers that may reference it.
        Debug.LogWarning("[LobbyManager] JoinGame() is deprecated. Use JoinRelayAsync() instead.");
    }

    public async void SetRelayJoinCode(string relayJoinCode) {
        try {
            Debug.Log("SetRelayJoinCode " + relayJoinCode);

            Lobby lobby = await Lobbies.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions {
                Data = new Dictionary<string, DataObject> {
                    { KEY_RELAY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            });

            joinedLobby = lobby;
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    // ── Phase 2: Real-Time Invite System ─────────────────────────────────────

    /// <summary>
    /// Initializes FriendsService (opens the messaging websocket) then registers
    /// the MessageReceived listener. Called after every successful authentication
    /// so the invite socket is open regardless of which UI the player is viewing.
    /// </summary>
    private async Task InitializeFriendsAndListenAsync()
    {
        try
        {
            await FriendsService.Instance.InitializeAsync();
            Debug.Log("[LobbyManager] FriendsService initialized — messaging socket open.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyManager] FriendsService.InitializeAsync failed: {e.Message}");
        }
        SetupInviteListener();
    }

    /// <summary>
    /// Subscribes to FriendsService.MessageReceived to detect incoming lobby invites.
    /// Safe to call multiple times — always unsubscribes first to prevent duplicate handlers.
    /// Must be called after FriendsService.Instance.InitializeAsync() has completed.
    /// </summary>
    private void SetupInviteListener()
    {
        try
        {
            FriendsService.Instance.MessageReceived -= HandleMessageReceived;
            FriendsService.Instance.MessageReceived += HandleMessageReceived;
            Debug.Log("[LobbyManager] FriendsService.MessageReceived listener registered.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyManager] SetupInviteListener failed (FriendsService may not be initialized yet): {e.Message}");
        }
    }

    /// <summary>
    /// FriendsService.MessageReceived callback — may arrive on a background thread.
    /// Deserializes the payload as LobbyInviteMessage, then marshals to the main thread.
    /// </summary>
    private void HandleMessageReceived(IMessageReceivedEvent e)
    {
        try
        {
            LobbyInviteMessage invite = e.GetAs<LobbyInviteMessage>();
            if (invite == null || string.IsNullOrEmpty(invite.LobbyId))
            {
                Debug.LogWarning("[LobbyManager] HandleMessageReceived — invite payload null or missing LobbyId.");
                return;
            }

            _pendingInviteLobbyId = invite.LobbyId;
            _pendingInviterName   = invite.InviterName ?? e.UserId;
            Debug.Log($"[LobbyManager] Invite message received from '{_pendingInviterName}' for lobby {_pendingInviteLobbyId}");
            Debug.Log($"[LobbyManager] OnInviteReceived subscriber count: {(OnInviteReceived == null ? 0 : OnInviteReceived.GetInvocationList().Length)}");

            string lobbyId     = _pendingInviteLobbyId;
            string inviterName = _pendingInviterName;

            Debug.Log("[LobbyManager] Enqueueing OnInviteReceived dispatch to main thread.");
            MainThreadDispatcher.Enqueue(() =>
            {
                Debug.Log("[LobbyManager] MainThreadDispatcher executing — raising OnInviteReceived.");
                OnInviteReceived?.Invoke(lobbyId, inviterName);
            });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LobbyManager] HandleMessageReceived — failed to parse invite: {ex.Message}");
        }
    }

    /// <summary>
    /// Joins a lobby by its ID (used by the invite accept flow — no code entry required).
    /// Resets alreadyStartedGame so HandleLobbyPolling can detect the relay code and
    /// call JoinRelayAsync exactly as it does for the join-by-code flow.
    /// </summary>
    public async Task JoinLobbyByIdAsync(string lobbyId)
    {
        Player player = GetPlayer();
        Lobby lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, new JoinLobbyByIdOptions
        {
            Player = player
        });
        joinedLobby        = lobby;
        LastLobbyCode      = lobby.LobbyCode ?? "";
        IsHost             = false;
        alreadyStartedGame = false; // reset so polling loop fires JoinRelayAsync
        Debug.Log($"[LobbyManager] JoinLobbyByIdAsync succeeded — lobby: {lobby.Name}");
        OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = lobby });
    }

}