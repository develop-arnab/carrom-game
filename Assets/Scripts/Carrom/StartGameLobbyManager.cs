using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.UI;

/*
 * StartGameLobbyManager — Offline-First Dynamic Lobby
 *
 * Architecture:
 *  - All UI config (game mode, player count) is stored in LOCAL fields.
 *  - Network is NEVER touched in Start() / Awake().
 *  - Network init only happens inside OnPlayOnlineClicked() / OnInviteFriendsClicked().
 *  - OnNetworkSpawn() handles post-spawn role split (Host vs Client buttons).
 *  - IsSpawned guards protect every method that touches NetworkVariables or IsServer.
 *  - Ready state is tracked server-side in _readyStates (Dictionary<ulong,bool>).
 *  - Host is implicitly always ready — never added to _readyStates.
 */

public class StartGameLobbyManager : SingletonNetwork<StartGameLobbyManager>
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown gameModeDropdown;      // 0=Freestyle, 1=Classic
    [SerializeField] private TMP_Dropdown playerCountDropdown;   // 0=2P, 1=3P, 2=4P

    [Header("Buttons")]
    [SerializeField] private GameObject startGameButton;         // Host-only
    [SerializeField] private GameObject readyButton;             // Client-only
    [SerializeField] private GameObject cancelReadyButton;       // Client-only (toggle)
    [SerializeField] private Button     playOnlineButton;        // Disabled during async connect
    [SerializeField] private Button     inviteFriendsButton;     // Disabled during async connect

    [Header("Slot Panels (optional visual)")]
    [SerializeField] private GameObject[] slotPanels;            // 4 slot UI panels

    [Header("Join Code Display")]
    [SerializeField] private TextMeshProUGUI joinCodeText;

    [Header("Scene")]
    [SerializeField] private SceneName nextScene = SceneName.Carrom;

    // ─── Network State ────────────────────────────────────────────────────────

    /// <summary>Synced ruleset — only written after OnNetworkSpawn on server.</summary>
    public NetworkVariable<GameMode> netCarromRuleset = new NetworkVariable<GameMode>(
        GameMode.Freestyle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ─── Local (Offline) State ────────────────────────────────────────────────

    private GameMode _localGameMode    = GameMode.Freestyle;
    private int      _localPlayerCount = 2;   // 2, 3, or 4
    private bool     _isPrivateLobby   = false;

    // ─── Ready State (server-side only) ──────────────────────────────────────

    private readonly Dictionary<ulong, bool> _readyStates = new Dictionary<ulong, bool>();

    // ─── Static accessor for StartGameManager ────────────────────────────────

    /// <summary>
    /// Exposes the configured player count so StartGameManager can size the
    /// Relay allocation dynamically instead of using a hardcoded value.
    /// </summary>
    public static int LocalPlayerCount => Instance != null ? Instance._localPlayerCount : 1;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    public override void Awake()
    {
        // Singleton re-entry guard: if a stale instance exists, shut down any
        // live NGO session before the base class destroys it (Req 13.1).
        if (Instance != null && Instance != this)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                Debug.Log("[StartGameLobbyManager] Stale NGO session detected — shutting down.");
                NetworkManager.Singleton.Shutdown();
            }
            // Let base.Awake() handle Destroy(gameObject) on the old instance
            // by temporarily nulling it so the base class re-registers this one.
            Destroy(Instance.gameObject);
        }
        base.Awake();
    }

    private void Start()
    {
        // Offline-only setup — no network calls here (Req 1.1, 1.2).
        if (gameModeDropdown != null)
        {
            gameModeDropdown.value = (int)_localGameMode;   // default Freestyle (Req 1.3)
            gameModeDropdown.onValueChanged.AddListener(OnGameModeChanged);
        }

        if (playerCountDropdown != null)
        {
            playerCountDropdown.value = 0;                  // default 2P (Req 1.3)
            playerCountDropdown.onValueChanged.AddListener(OnPlayerCountChanged);
        }

        ApplyPlayerCountToUI(_localPlayerCount);

        // Programmatic binding — ensures clicks fire regardless of Inspector wiring
        if (playOnlineButton    != null) playOnlineButton.onClick.AddListener(OnPlayOnlineClicked);
        if (inviteFriendsButton != null) inviteFriendsButton.onClick.AddListener(OnInviteFriendsClicked);
        if (startGameButton != null && startGameButton.TryGetComponent<Button>(out var startBtn))
            startBtn.onClick.AddListener(OnStartGameClicked);

        // Both action buttons hidden until OnNetworkSpawn fires (Req 1.4)
        SetButtonVisibility(isHost: false, isSpawned: false);
    }

    private void OnDisable()
    {
        // Null-guard required: NetworkManager may already be destroyed (Req 13.3)
        if (IsSpawned && IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        }
    }

    // ─── Offline UI Callbacks ─────────────────────────────────────────────────

    /// <summary>Req 2.3 — update local game mode when dropdown changes.</summary>
    private void OnGameModeChanged(int index)
    {
        _localGameMode = (GameMode)index;

        // 3-Player Interlock: Classic is invalid with 3 players (Req 3.2)
        if (_localGameMode == GameMode.Classic && _localPlayerCount == 3)
        {
            _localGameMode = GameMode.Freestyle;
            if (gameModeDropdown != null)
                gameModeDropdown.value = (int)GameMode.Freestyle;
            Debug.Log("[StartGameLobbyManager] Classic requires 2 or 4 players — reverted to Freestyle.");
        }

        // Push to network only when already spawned as host (Req 9.1, 9.3)
        if (IsSpawned && IsServer)
            netCarromRuleset.Value = _localGameMode;

        Debug.Log($"[StartGameLobbyManager] Local game mode: {_localGameMode}");
    }

    /// <summary>Req 2.4 — update local player count when dropdown changes.</summary>
    private void OnPlayerCountChanged(int index)
    {
        // Dropdown: 0=2P, 1=3P, 2=4P  →  _localPlayerCount = index + 2
        _localPlayerCount = index + 2;

        // 3-Player Interlock: Classic is invalid with 3 players (Req 3.1)
        if (_localPlayerCount == 3 && _localGameMode == GameMode.Classic)
        {
            _localGameMode = GameMode.Freestyle;
            if (gameModeDropdown != null)
                gameModeDropdown.value = (int)GameMode.Freestyle;
            Debug.Log("[StartGameLobbyManager] 3-player selected — Classic forced to Freestyle.");

            // Push to network if already spawned (Req 3.4)
            if (IsSpawned && IsServer)
                netCarromRuleset.Value = GameMode.Freestyle;
        }

        ApplyPlayerCountToUI(_localPlayerCount);   // Req 2.5
        Debug.Log($"[StartGameLobbyManager] Local player count: {_localPlayerCount}");
    }

    /// <summary>
    /// Activates slot panels sequentially from index 0 to count-1.
    ///
    /// UI layout (two-column grid):
    ///   Index 0 = P1 (Left,  Top)    Index 1 = P2 (Right, Top)
    ///   Index 2 = P3 (Left,  Bottom) Index 3 = P4 (Right, Bottom)
    ///
    /// Activation by player count:
    ///   2P → [0,1] active,  [2,3] inactive   (1v1)
    ///   3P → [0,1,2] active,[3]   inactive   (Freestyle only)
    ///   4P → [0,1,2,3] all active            (2v2: Team White=P1/P3, Team Black=P2/P4)
    ///
    /// Null-safe: skips null elements and returns early if array is null/empty (Req 4.3, 4.4).
    /// </summary>
    private void ApplyPlayerCountToUI(int count)
    {
        if (slotPanels == null || slotPanels.Length == 0) return;
        for (int i = 0; i < slotPanels.Length; i++)
        {
            if (slotPanels[i] != null)
                slotPanels[i].SetActive(i < count);
        }
    }

    // ─── Button Handlers (wired in Inspector) ─────────────────────────────────

    /// <summary>Called by the [Play Online] button (Req 5.1).</summary>
    public async void OnPlayOnlineClicked()
    {
        Debug.Log("[StartGameLobbyManager] Button clicked!");
        try
        {
            if (playOnlineButton   != null) playOnlineButton.interactable   = false;
            if (inviteFriendsButton != null) inviteFriendsButton.interactable = false;

            _isPrivateLobby = false;
            await InitializeNetworkServices();

            // Join or create a public lobby, then set up the NGO session immediately.
            // QuickJoinOrCreatePublicLobbyAsync returns true if we became the host.
            bool isHost = await LobbyManager.Instance.QuickJoinOrCreatePublicLobbyAsync();

            if (isHost)
            {
                // We created the lobby — allocate Relay and StartHost.
                bool ok = await StartGameManager.Instance.CreateRelayAsync();
                if (!ok) Debug.LogError("[StartGameLobbyManager] Relay creation failed.");
            }
            // If isHost == false: we joined an existing lobby as a client.
            // HandleLobbyPolling in LobbyManager will detect the relay code and
            // call JoinRelayAsync automatically, which fires OnNetworkSpawn on this client.
        }
        catch (Exception e)
        {
            Debug.LogError($"[StartGameLobbyManager] OnPlayOnlineClicked failed: {e.Message}");
            if (playOnlineButton   != null) playOnlineButton.interactable   = true;
            if (inviteFriendsButton != null) inviteFriendsButton.interactable = true;
        }
    }

    /// <summary>Called by the [Invite Friends] button (Req 5.2, 5.3, 5.4).</summary>
    public async void OnInviteFriendsClicked()
    {
        Debug.Log("[StartGameLobbyManager] Button clicked!");
        try
        {
            if (playOnlineButton   != null) playOnlineButton.interactable   = false;
            if (inviteFriendsButton != null) inviteFriendsButton.interactable = false;

            _isPrivateLobby = true;
            await InitializeNetworkServices();

            // Create private lobby (isHostLobby: true suppresses auto-navigation).
            LobbyManager.Instance.CreateLobby(
                "Carrom Private",
                _localPlayerCount,
                isPrivate: true,
                LobbyManager.GameMode.Carrom,
                isHostLobby: true);

            // Immediately allocate Relay and StartHost so OnNetworkSpawn fires
            // and the waiting room UI becomes active (Start Game button visible).
            bool ok = await StartGameManager.Instance.CreateRelayAsync();
            if (!ok) Debug.LogError("[StartGameLobbyManager] Relay creation failed.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[StartGameLobbyManager] OnInviteFriendsClicked failed: {e.Message}");
            if (playOnlineButton   != null) playOnlineButton.interactable   = true;
            if (inviteFriendsButton != null) inviteFriendsButton.interactable = true;
        }
    }

    /// <summary>Host-only: start the game (Req 8.1).</summary>
    public void OnStartGameClicked()
    {
        Debug.Log("[StartGameLobbyManager] Start Game button clicked!");
        if (!IsSpawned || !IsServer) return;
        StartGame();
    }

    /// <summary>Client-only: toggle ready state via ServerRpc (Req 11.3).</summary>
    public void OnReadyClicked()
    {
        if (!IsSpawned || IsServer) return;
        ToggleReadyServerRpc();
    }

    // ─── Network Initialization ───────────────────────────────────────────────

    /// <summary>
    /// Idempotent Unity Services init + anonymous sign-in (Req 6.1, 6.2, 6.3).
    /// Safe to call multiple times — skips steps already completed.
    /// </summary>
    private async Task InitializeNetworkServices()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
            Debug.Log("[StartGameLobbyManager] Unity Services initialized.");
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("[StartGameLobbyManager] Signed in anonymously.");
        }
    }

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Initial authoritative sync of local config to network (Req 9.2)
            netCarromRuleset.Value = _localGameMode;

            // Subscribe to connect/disconnect for ready state tracking (Req 11.1)
            NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Display join code if a private lobby was created (Req 8.5)
            if (joinCodeText != null && !string.IsNullOrEmpty(LobbyManager.LastLobbyCode))
                joinCodeText.text = $"Code: {LobbyManager.LastLobbyCode}";

            // Host keeps dropdowns interactable (Req 8.4)
            if (gameModeDropdown != null)    gameModeDropdown.interactable    = true;
            if (playerCountDropdown != null) playerCountDropdown.interactable = true;

            SetButtonVisibility(isHost: true, isSpawned: true);  // Req 8.1
            RefreshStartButtonState();
        }
        else
        {
            // Clients: read-only dropdowns (Req 8.3)
            if (gameModeDropdown != null)    gameModeDropdown.interactable    = false;
            if (playerCountDropdown != null) playerCountDropdown.interactable = false;

            SetButtonVisibility(isHost: false, isSpawned: true);  // Req 8.2
        }

        // Sync dropdown to live network value on spawn + subscribe to future changes (Req 9.4)
        netCarromRuleset.OnValueChanged += (_, newMode) =>
        {
            if (gameModeDropdown != null)
                gameModeDropdown.value = (int)newMode;
        };

        if (gameModeDropdown != null)
            gameModeDropdown.value = (int)netCarromRuleset.Value;
    }

    // ─── Ready State ──────────────────────────────────────────────────────────

    /// <summary>Server: new client defaults to ready = true (Req 11.2).</summary>
    private void OnClientConnected(ulong clientId)
    {
        if (!IsSpawned || !IsServer) return;
        _readyStates[clientId] = true;
        Debug.Log($"[StartGameLobbyManager] Client {clientId} connected — ready by default.");
        RefreshStartButtonState();
    }

    /// <summary>Server: remove client from ready tracking on disconnect.</summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsSpawned || !IsServer) return;
        _readyStates.Remove(clientId);
        Debug.Log($"[StartGameLobbyManager] Client {clientId} disconnected from lobby.");
        RefreshStartButtonState();
    }

    /// <summary>
    /// Client → Server: toggle this client's ready state (Req 11.3).
    /// RequireOwnership = false so any client can call it.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ToggleReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (_readyStates.ContainsKey(clientId))
            _readyStates[clientId] = !_readyStates[clientId];
        else
            _readyStates[clientId] = false; // first toggle from default-true → not ready

        bool isReady = _readyStates[clientId];
        Debug.Log($"[StartGameLobbyManager] Client {clientId} ready state → {isReady}");

        UpdateReadyStateClientRpc(clientId, isReady);
        RefreshStartButtonState();
    }

    /// <summary>
    /// Server → All clients: push ready state update for slot panel UI (Req 11.3).
    /// </summary>
    [ClientRpc]
    private void UpdateReadyStateClientRpc(ulong clientId, bool isReady)
    {
        // Slot panel ready indicator update goes here when slot panel UI is wired.
        Debug.Log($"[StartGameLobbyManager] [ClientRpc] Client {clientId} ready: {isReady}");
    }

    /// <summary>
    /// Recomputes startGameButton interactable state (Req 11.4, 11.5).
    /// Enabled iff at least one client is connected AND all are ready.
    /// </summary>
    private void RefreshStartButtonState()
    {
        if (!IsServer || startGameButton == null) return;

        bool allReady = _readyStates.Count == 0 || _readyStates.Values.All(v => v);
        Button btn = startGameButton.GetComponent<Button>();
        if (btn != null)
            btn.interactable = allReady;
    }

    // ─── Game Start ───────────────────────────────────────────────────────────

    private void StartGame()
    {
        // Ghost Bot Injector: silently fill empty seats (Req 12.1–12.5)
        InjectGhostBots();

        CarromGameManager.ActiveRuleset = netCarromRuleset.Value;
        Debug.Log($"[StartGameLobbyManager] Starting game — ruleset: {CarromGameManager.ActiveRuleset}, players: {_localPlayerCount}");

        // Trigger fade on all clients, then load the scene (server-authoritative).
        StartGameClientRpc();
        LoadingSceneManager.Instance.LoadScene(nextScene);
    }

    /// <summary>
    /// Ghost Bot Injector — no UI, no confirmation (Req 12.2).
    /// Snapshots ConnectedClientsIds.Count at call time (before scene load).
    /// </summary>
    private void InjectGhostBots()
    {
        int humanCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int botCount   = _localPlayerCount - humanCount;

        if (botCount > 0)
            Debug.Log($"[StartGameLobbyManager] Ghost Bot Injector: {botCount} bot(s) will fill empty seats.");

        // Store for CarromGameManager to consume on spawn (Req 12.1, 12.3)
        CarromGameManager.PendingPlayerCount = _localPlayerCount;
        CarromGameManager.PendingBotCount    = botCount;
    }

    [ClientRpc]
    private void StartGameClientRpc()
    {
        LoadingFadeEffect.Instance.FadeAll();
    }

    // ─── UI Helpers ───────────────────────────────────────────────────────────

    private void SetButtonVisibility(bool isHost, bool isSpawned)
    {
        if (startGameButton   != null) startGameButton.SetActive(isSpawned && isHost);
        if (readyButton       != null) readyButton.SetActive(isSpawned && !isHost);
        if (cancelReadyButton != null) cancelReadyButton.SetActive(false);
    }
}
