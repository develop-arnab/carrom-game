using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using Unity.Netcode;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Relay;
using UnityEngine;

/*
 * StartGameManager — Relay + NGO session setup
 *
 * Lifecycle (new):
 *   CreateRelayAsync()  → allocate Relay → StartHost() → write join code to Lobby
 *   JoinRelayAsync()    → join Relay allocation → StartClient()
 *
 * IMPORTANT: Neither method loads a scene. Scene loading is owned exclusively
 * by StartGameLobbyManager.StartGame() so the host/client can sit in the
 * waiting room after the NGO session starts.
 *
 * The old OnLobbyStartGame event path is kept for backward compatibility with
 * any non-Carrom scenes that still use the legacy flow.
 */
public class StartGameManager : MonoBehaviour
{
    public static StartGameManager Instance { get; private set; }

    [SerializeField] private SceneName nextScene = SceneName.CharacterSelection;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Legacy event path — still used by non-Carrom lobby flows.
        LobbyManager.Instance.OnLobbyStartGame += LobbyManager_OnLobbyStartGame;
    }

    // ─── Legacy event handler (non-Carrom flows) ──────────────────────────────

    private void LobbyManager_OnLobbyStartGame(object sender, LobbyManager.LobbyEventArgs e)
    {
        if (LobbyManager.IsHost)
            _ = CreateRelayAsync();
        else
            _ = JoinRelayAsync(LobbyManager.RelayJoinCode);
    }

    // ─── Public async API (called directly by StartGameLobbyManager) ──────────

    /// <summary>
    /// Allocates a Relay, configures UnityTransport, calls StartHost(), and
    /// writes the join code back to the Unity Lobby so clients can find it.
    /// Does NOT load any scene — the caller decides when to transition.
    /// Returns true on success, false on failure.
    /// </summary>
    public async Task<bool> CreateRelayAsync()
    {
        try
        {
            int maxConnections = Mathf.Max(1, StartGameLobbyManager.LocalPlayerCount - 1);
            Debug.Log($"[StartGameManager] Allocating Relay for {maxConnections} client connection(s).");

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[StartGameManager] Relay join code: {joinCode}");

            RelayServerData relayServerData = new RelayServerData(allocation, "wss");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // Start the NGO host session — stays in current scene.
            NetworkManager.Singleton.StartHost();
            Debug.Log("[StartGameManager] StartHost() called — waiting in lobby.");

            // Publish the join code so polling clients can connect.
            LobbyManager.Instance.SetRelayJoinCode(joinCode);
            return true;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[StartGameManager] CreateRelayAsync failed: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Joins an existing Relay allocation and calls StartClient().
    /// Does NOT load any scene.
    /// Returns true on success, false on failure.
    /// </summary>
    public async Task<bool> JoinRelayAsync(string joinCode)
    {
        try
        {
            Debug.Log($"[StartGameManager] Joining Relay with code: {joinCode}");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData relayServerData = new RelayServerData(joinAllocation, "wss");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient();
            Debug.Log("[StartGameManager] StartClient() called — joining lobby session.");
            return true;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[StartGameManager] JoinRelayAsync failed: {e.Message}");
            return false;
        }
    }

    // ─── Legacy helpers (kept for non-Carrom flows) ───────────────────────────

    /// <summary>Legacy: StartHost + immediate scene load. Used by old 1v1 flow.</summary>
    public void StartHost()
    {
        NetworkManager.Singleton.StartHost();
        LoadingSceneManager.Instance.Init();
        LoadingSceneManager.Instance.LoadScene(nextScene);
    }

    /// <summary>Legacy: StartClient only.</summary>
    public void StartClient()
    {
        NetworkManager.Singleton.StartClient();
    }
}
