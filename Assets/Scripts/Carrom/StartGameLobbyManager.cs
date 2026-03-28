using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Friends;
using UnityEngine;
using UnityEngine.UI;

/*
 * StartGameLobbyManager — Offline-First Dynamic Lobby with "Illusion of Life" UI
 *
 * Illusion of Life flows:
 *  Flow A — Host slot 0 populated on OnNetworkSpawn with UGS name + avatar.
 *  Flow B — Human client join: server requests name via RPC, broadcasts to all.
 *  Flow C — MATCH button: fills vacant slots with FakeIdentityGenerator names,
 *            synced to all clients via ClientRpc so both sides see fake humans.
 */

// Slot UI descriptor — mirrors CharacterContainer from CharacterSelectionManager
[Serializable]
public struct LobbySlot
{
    public GameObject      waitingText;     // "WAITING..." overlay
    public Image           avatarImage;     // Player avatar
    public TextMeshProUGUI nameText;        // Display name
    public GameObject      readyIndicator; // Optional ready badge
}

public class StartGameLobbyManager : SingletonNetwork<StartGameLobbyManager>
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown gameModeDropdown;
    [SerializeField] private TMP_Dropdown playerCountDropdown;

    [Header("Buttons")]
    [SerializeField] private GameObject startGameButton;
    [SerializeField] private GameObject readyButton;
    [SerializeField] private GameObject cancelReadyButton;
    [SerializeField] private Button     playOnlineButton;
    [SerializeField] private Button     inviteFriendsButton;
    [SerializeField] private Button     matchButton;

    [Header("Lobby Slots (4 max)")]
    [SerializeField] private LobbySlot[] slots = new LobbySlot[4];

    [Header("Default Avatars")]
    [SerializeField] private Sprite[] defaultAvatars;

    [Header("Join Code Display")]
    [SerializeField] private TextMeshProUGUI joinCodeText;

    [Header("Scene")]
    [SerializeField] private SceneName nextScene = SceneName.Carrom;

    // ── Network State ─────────────────────────────────────────────────────────

    public NetworkVariable<GameMode> netCarromRuleset = new NetworkVariable<GameMode>(
        GameMode.Freestyle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local State ───────────────────────────────────────────────────────────

    private GameMode _localGameMode    = GameMode.Freestyle;
    private int      _localPlayerCount = 2;
    private bool     _isPrivateLobby   = false;

    // ── Ready State (server-side only) ────────────────────────────────────────

    private readonly Dictionary<ulong, bool> _readyStates = new Dictionary<ulong, bool>();

    // ── Slot occupancy (all clients) ──────────────────────────────────────────

    private readonly string[] _slotNames = new string[4];

    // ── Static accessor ───────────────────────────────────────────────────────

    public static int LocalPlayerCount => Instance != null ? Instance._localPlayerCount : 1;

    // ── Fake Identity Generator ───────────────────────────────────────────────

    private static readonly string[] _namePrefixes =
    {
        "Guest", "Shadow", "Sniper", "Carrom", "King", "Ace", "Pro",
        "Flash", "Storm", "Blaze", "Ghost", "Ninja", "Viper", "Hawk"
    };

    private static readonly string[] _nameSuffixes =
    {
        "X", "King", "Master", "99", "Pro", "Star", "Boss",
        "Striker", "Legend", "Champ", "Wizard", "Hunter"
    };

    private static string GenerateFakeName()
    {
        string prefix = _namePrefixes[UnityEngine.Random.Range(0, _namePrefixes.Length)];
        if (UnityEngine.Random.value < 0.5f)
            return $"{prefix}_{UnityEngine.Random.Range(1000, 9999)}";
        return $"{prefix}{_nameSuffixes[UnityEngine.Random.Range(0, _nameSuffixes.Length)]}";
    }

    private Sprite GetRandomAvatar()
    {
        if (defaultAvatars == null || defaultAvatars.Length == 0) return null;
        return defaultAvatars[UnityEngine.Random.Range(0, defaultAvatars.Length)];
    }

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    public override void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                Debug.Log("[StartGameLobbyManager] Stale NGO session — shutting down.");
                NetworkManager.Singleton.Shutdown();
            }
            Destroy(Instance.gameObject);
        }
        base.Awake();
    }

    private async void Start()
    {
        // Step 1: Wire up UI that doesn't need auth first — instant, no blocking.
        if (gameModeDropdown != null)
        {
            gameModeDropdown.value = (int)_localGameMode;
            gameModeDropdown.onValueChanged.AddListener(OnGameModeChanged);
        }

        if (playerCountDropdown != null)
        {
            playerCountDropdown.value = 0;
            playerCountDropdown.onValueChanged.AddListener(OnPlayerCountChanged);
        }

        ApplyPlayerCountToUI(_localPlayerCount);
        ResetAllSlotsToWaiting();

        // Only MATCH and Start are still relevant — Online/Invite are deprecated.
        if (matchButton != null) matchButton.onClick.AddListener(OnMatchClicked);
        if (startGameButton != null && startGameButton.TryGetComponent<Button>(out var startBtn))
            startBtn.onClick.AddListener(OnStartGameClicked);

        SetButtonVisibility(isHost: false, isSpawned: false);

        // Step 2: Authenticate — awaited so PlayerName is ready before slot population.
        try
        {
            await InitializeNetworkServices();
            Debug.Log("[StartGameLobbyManager] UGS ready on scene load.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StartGameLobbyManager] UGS init on Start failed: {e.Message}");
        }

        // Step 3: Populate local slot immediately — offline, no network dependency.
        InitHostSlot();

        // Step 4: Kick off the private lobby creation in the background.
        // Fire-and-forget: does NOT block the rest of the UI or scene animations.
        if (joinCodeText != null) joinCodeText.text = "Generating Code...";
        _ = AutoCreatePrivateLobbyAsync();
    }

    private void OnDisable()
    {
        if (IsSpawned && IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        }
    }

    // ── Slot UI Primitives ────────────────────────────────────────────────────

    /// <summary>
    /// Resets a slot to the empty "WAITING..." state.
    /// Mirrors SetNonPlayableChar() from CharacterSelectionManager.
    /// </summary>
    private void SetSlotWaiting(int i)
    {
        if (i < 0 || i >= slots.Length) return;
        if (slots[i].waitingText    != null) slots[i].waitingText.SetActive(true);
        if (slots[i].avatarImage    != null) slots[i].avatarImage.gameObject.SetActive(false);
        if (slots[i].nameText       != null) slots[i].nameText.text = "";
        if (slots[i].readyIndicator != null) slots[i].readyIndicator.SetActive(false);
        _slotNames[i] = "";
    }

    /// <summary>
    /// Populates a slot with a name and avatar.
    /// Mirrors SetPlayebleChar() from CharacterSelectionManager.
    /// </summary>
    private void SetSlotOccupied(int i, string displayName, Sprite avatar = null)
    {
        if (i < 0 || i >= slots.Length) return;
        if (slots[i].waitingText != null) slots[i].waitingText.SetActive(false);
        if (slots[i].nameText    != null) slots[i].nameText.text = displayName;
        if (slots[i].avatarImage != null)
        {
            slots[i].avatarImage.gameObject.SetActive(true);
            // Reset alpha to fully opaque — prefab default may be transparent (alpha=0)
            // mirroring CharacterSelectionManager's SetNonPlayableChar color reset pattern.
            slots[i].avatarImage.color = Color.white;
            if (avatar != null) slots[i].avatarImage.sprite = avatar;
        }
        _slotNames[i] = displayName;
    }

    private void ResetAllSlotsToWaiting()
    {
        for (int i = 0; i < slots.Length; i++) SetSlotWaiting(i);
    }

    private int FindNextVacantSlot()
    {
        for (int i = 0; i < _localPlayerCount && i < slots.Length; i++)
            if (string.IsNullOrEmpty(_slotNames[i])) return i;
        return -1;
    }

    // ── Flow A: Host Initialization ───────────────────────────────────────────

    private void InitHostSlot()
    {
        string name = AuthenticationService.Instance.PlayerName
                   ?? AuthenticationService.Instance.PlayerId
                   ?? "Host";
        SetSlotOccupied(0, name, GetRandomAvatar());
        Debug.Log($"[StartGameLobbyManager] Flow A — Host slot 0: '{name}'");
    }

    // ── Flow B: Human Client Connection ──────────────────────────────────────

    private void OnClientConnected(ulong clientId)
    {
        if (!IsSpawned || !IsServer) return;

        // Guard: the host's own connection fires this callback too.
        // Slot 0 is already populated by InitHostSlot() — skip it here.
        if (clientId == NetworkManager.ServerClientId) return;

        _readyStates[clientId] = true;
        Debug.Log($"[StartGameLobbyManager] Client {clientId} connected — syncing lobby state then requesting name.");

        ClientRpcParams toNewClient = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        // Bug B fix: push every already-occupied slot to the new client so they
        // see the host (and any other players) that joined before them.
        for (int i = 0; i < _slotNames.Length; i++)
        {
            if (!string.IsNullOrEmpty(_slotNames[i]))
            {
                Debug.Log($"[StartGameLobbyManager] Late-join sync → client {clientId}: slot {i} = '{_slotNames[i]}'");
                SyncSlotToClientRpc(i, _slotNames[i], toNewClient);
            }
        }

        // Now ask the new client for their own name so we can fill the next vacant slot.
        RequestClientNameClientRpc(toNewClient);
        RefreshStartButtonState();
    }

    /// <summary>
    /// Targeted RPC that syncs a single already-occupied slot to one specific client.
    /// Used for late-joiner state catch-up — avoids broadcasting to everyone.
    /// </summary>
    [ClientRpc]
    private void SyncSlotToClientRpc(int slotIndex, string displayName, ClientRpcParams clientRpcParams = default)
    {
        SetSlotOccupied(slotIndex, displayName, GetRandomAvatar());
    }

    [ClientRpc]
    private void RequestClientNameClientRpc(ClientRpcParams clientRpcParams = default)
    {
        string name = AuthenticationService.Instance.PlayerName
                   ?? AuthenticationService.Instance.PlayerId
                   ?? "Player";
        ReportClientNameServerRpc(name);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportClientNameServerRpc(string playerName, ServerRpcParams rpcParams = default)
    {
        int slot = FindNextVacantSlot();
        if (slot < 0)
        {
            Debug.LogWarning("[StartGameLobbyManager] Flow B — no vacant slot for incoming client.");
            return;
        }
        Debug.Log($"[StartGameLobbyManager] Flow B — slot {slot}: '{playerName}'");
        OccupySlotClientRpc(slot, playerName);
    }

    // ── Flow C: MATCH Button ──────────────────────────────────────────────────

    public void OnMatchClicked()
    {
        if (!IsSpawned || !IsServer) return;
        Debug.Log("[StartGameLobbyManager] Flow C — MATCH: filling vacant slots.");

        for (int i = 0; i < _localPlayerCount && i < slots.Length; i++)
        {
            if (!string.IsNullOrEmpty(_slotNames[i])) continue;
            string fakeName = GenerateFakeName();
            Debug.Log($"[StartGameLobbyManager] Flow C — slot {i}: '{fakeName}'");
            OccupySlotClientRpc(i, fakeName);
        }

        int humanCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        CarromGameManager.PendingBotCount = _localPlayerCount - humanCount;
    }

    // ── Shared Slot Sync RPC ──────────────────────────────────────────────────

    [ClientRpc]
    private void OccupySlotClientRpc(int slotIndex, string displayName)
    {
        SetSlotOccupied(slotIndex, displayName, GetRandomAvatar());
    }

    // ── Offline UI Callbacks ──────────────────────────────────────────────────

    private void OnGameModeChanged(int index)
    {
        _localGameMode = (GameMode)index;

        if (_localGameMode == GameMode.Classic && _localPlayerCount == 3)
        {
            _localGameMode = GameMode.Freestyle;
            if (gameModeDropdown != null)
                gameModeDropdown.value = (int)GameMode.Freestyle;
            Debug.Log("[StartGameLobbyManager] Classic requires 2 or 4 players — reverted to Freestyle.");
        }

        if (IsSpawned && IsServer)
            netCarromRuleset.Value = _localGameMode;

        Debug.Log($"[StartGameLobbyManager] Local game mode: {_localGameMode}");
    }

    private void OnPlayerCountChanged(int index)
    {
        _localPlayerCount = index + 2;

        if (_localPlayerCount == 3 && _localGameMode == GameMode.Classic)
        {
            _localGameMode = GameMode.Freestyle;
            if (gameModeDropdown != null)
                gameModeDropdown.value = (int)GameMode.Freestyle;
            Debug.Log("[StartGameLobbyManager] 3-player selected — Classic forced to Freestyle.");

            if (IsSpawned && IsServer)
                netCarromRuleset.Value = GameMode.Freestyle;
        }

        ApplyPlayerCountToUI(_localPlayerCount);
        Debug.Log($"[StartGameLobbyManager] Local player count: {_localPlayerCount}");
    }

    private void ApplyPlayerCountToUI(int count)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            // The slot root is the parent of waitingText; fall back to avatarImage parent
            Transform root = slots[i].waitingText  != null ? slots[i].waitingText.transform.parent
                           : slots[i].avatarImage  != null ? slots[i].avatarImage.transform.parent
                           : null;
            if (root != null) root.gameObject.SetActive(i < count);
        }
    }

    // ── Auto-Host (Background Task) ──────────────────────────────────────────

    /// <summary>
    /// Silently creates a private lobby and starts the NGO Host session in the
    /// background. Called fire-and-forget from Start() so it never blocks the UI.
    /// On success, joinCodeText updates to the real join code.
    /// </summary>
    private async Task AutoCreatePrivateLobbyAsync()
    {
        try
        {
            Debug.Log("[StartGameLobbyManager] AutoHost — creating private lobby...");

            LobbyManager.Instance.CreateLobby(
                "Carrom Private",
                _localPlayerCount,
                isPrivate: true,
                LobbyManager.GameMode.Carrom,
                isHostLobby: true);

            bool ok = await StartGameManager.Instance.CreateRelayAsync();
            if (!ok)
            {
                Debug.LogError("[StartGameLobbyManager] AutoHost — Relay creation failed.");
                if (joinCodeText != null) joinCodeText.text = "Code: Error";
                return;
            }

            // NGO Host is now running — OnNetworkSpawn will fire and update the join code.
            Debug.Log("[StartGameLobbyManager] AutoHost — NGO Host started successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[StartGameLobbyManager] AutoHost failed: {e.Message}");
            if (joinCodeText != null) joinCodeText.text = "Code: Error";
        }
    }

    // ── Deprecated Button Handlers (kept as stubs for scene compatibility) ────

    /// <summary>Deprecated — auto-hosting replaces this flow.</summary>
    public void OnPlayOnlineClicked()
    {
        Debug.Log("[StartGameLobbyManager] OnPlayOnlineClicked — deprecated, auto-host is active.");
    }

    /// <summary>Opens the Friends panel via PanelManager.</summary>
    public void OnInviteFriendsClicked()
    {
        Debug.Log("[StartGameLobbyManager] OnInviteFriendsClicked fired.");
        try
        {
            PanelManager.Open("friends");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[StartGameLobbyManager] OnInviteFriendsClicked failed: {e.Message}\n{e.StackTrace}");
        }
    }

    public void OnStartGameClicked()
    {
        if (!IsSpawned || !IsServer) return;
        StartGame();
    }

    public void OnReadyClicked()
    {
        if (!IsSpawned || IsServer) return;
        ToggleReadyServerRpc();
    }

    // ── Network Initialization ────────────────────────────────────────────────

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

        // Initialize Friends service — safe to call even if already initialized.
        try
        {
            await FriendsService.Instance.InitializeAsync();
            Debug.Log("[StartGameLobbyManager] Friends service initialized.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StartGameLobbyManager] Friends service init skipped: {e.Message}");
        }
    }

    // ── NGO Lifecycle ─────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            netCarromRuleset.Value = _localGameMode;

            NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            if (joinCodeText != null && !string.IsNullOrEmpty(LobbyManager.LastLobbyCode))
                joinCodeText.text = $"Code: {LobbyManager.LastLobbyCode}";

            if (gameModeDropdown    != null) gameModeDropdown.interactable    = true;
            if (playerCountDropdown != null) playerCountDropdown.interactable = true;

            SetButtonVisibility(isHost: true, isSpawned: true);
            RefreshStartButtonState();
            // Flow A is handled in Start() — slot 0 is already populated offline.
            // No InitHostSlot() call needed here.
        }
        else
        {
            if (gameModeDropdown    != null) gameModeDropdown.interactable    = false;
            if (playerCountDropdown != null) playerCountDropdown.interactable = false;

            SetButtonVisibility(isHost: false, isSpawned: true);
        }

        netCarromRuleset.OnValueChanged += (_, newMode) =>
        {
            if (gameModeDropdown != null)
                gameModeDropdown.value = (int)newMode;
        };

        if (gameModeDropdown != null)
            gameModeDropdown.value = (int)netCarromRuleset.Value;
    }

    // ── Ready State ───────────────────────────────────────────────────────────

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsSpawned || !IsServer) return;
        _readyStates.Remove(clientId);
        Debug.Log($"[StartGameLobbyManager] Client {clientId} disconnected.");
        RefreshStartButtonState();
    }

    [ServerRpc(RequireOwnership = false)]
    public void ToggleReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (_readyStates.ContainsKey(clientId))
            _readyStates[clientId] = !_readyStates[clientId];
        else
            _readyStates[clientId] = false;

        bool isReady = _readyStates[clientId];
        Debug.Log($"[StartGameLobbyManager] Client {clientId} ready → {isReady}");

        UpdateReadyStateClientRpc(clientId, isReady);
        RefreshStartButtonState();
    }

    [ClientRpc]
    private void UpdateReadyStateClientRpc(ulong clientId, bool isReady)
    {
        Debug.Log($"[StartGameLobbyManager] [ClientRpc] Client {clientId} ready: {isReady}");
        // Wire ready indicator per slot here if needed
    }

    private void RefreshStartButtonState()
    {
        if (!IsServer || startGameButton == null) return;
        bool allReady = _readyStates.Count == 0 || _readyStates.Values.All(v => v);
        if (startGameButton.TryGetComponent<Button>(out var btn))
            btn.interactable = allReady;
    }

    // ── Game Start ────────────────────────────────────────────────────────────

    private void StartGame()
    {
        InjectGhostBots();
        CarromGameManager.ActiveRuleset = netCarromRuleset.Value;
        Debug.Log($"[StartGameLobbyManager] Starting — ruleset: {CarromGameManager.ActiveRuleset}");
        StartGameClientRpc();
        LoadingSceneManager.Instance.LoadScene(nextScene);
    }

    private void InjectGhostBots()
    {
        int humanCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int botCount   = _localPlayerCount - humanCount;
        if (botCount > 0)
            Debug.Log($"[StartGameLobbyManager] Ghost Bot Injector: {botCount} bot(s).");
        CarromGameManager.PendingPlayerCount = _localPlayerCount;
        CarromGameManager.PendingBotCount    = botCount;
    }

    [ClientRpc]
    private void StartGameClientRpc()
    {
        LoadingFadeEffect.Instance.FadeAll();
    }

    // ── UI Helpers ────────────────────────────────────────────────────────────

    private void SetButtonVisibility(bool isHost, bool isSpawned)
    {
        if (startGameButton   != null) startGameButton.SetActive(isSpawned && isHost);
        if (readyButton       != null) readyButton.SetActive(isSpawned && !isHost);
        if (cancelReadyButton != null) cancelReadyButton.SetActive(false);
        // MATCH button is host-only and only useful while spawned
        if (matchButton != null) matchButton.gameObject.SetActive(isSpawned && isHost);
    }
}
