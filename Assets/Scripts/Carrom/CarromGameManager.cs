using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public enum GameResult { HostWins, ClientWins, Draw }

public class CarromGameManager : NetworkBehaviour
{
    public NetworkVariable<int>   networkScorePlayer = new NetworkVariable<int>(0,     NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int>   networkScoreEnemy  = new NetworkVariable<int>(0,     NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> networkTimeLeft    = new NetworkVariable<float>(120f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool>  networkPlayerTurn  = new NetworkVariable<bool>(true,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    bool gameOver = false;
    bool isPaused = false;

    [SerializeField] TextMeshProUGUI scoreTextEnemy;
    [SerializeField] TextMeshProUGUI scoreTextPlayer;
    [SerializeField] TextMeshProUGUI gameOverText;
    [SerializeField] TextMeshProUGUI instructionsText;

    [SerializeField] GameObject instructionsMenu;
    [SerializeField] GameObject pauseMenu;
    [SerializeField] GameObject gameOverMenu;
    [SerializeField] GameObject activeStriker;
    [SerializeField] GameObject turnText;
    [SerializeField] GameObject slider;
    [SerializeField] Animator   animator;

    [Header("Telemetry System")]
    [SerializeField] private TelemetryRecorder              telemetryRecorder;
    [SerializeField] private BatchTransmitter               batchTransmitter;
    [SerializeField] private PieceRegistry                  pieceRegistry;
    [SerializeField] private Carrom.Telemetry.PlaybackEngine playbackEngine;

    [SerializeField]
    [Tooltip("Minimum velocity magnitude for a piece to be considered moving")]
    private float velocityThreshold = 0.1f;

    TimerScript timerScript;
    private const string FirstTimeLaunchKey = "FirstTimeLaunch";

    // -------------------------------------------------------------------------
    // UNITY LIFECYCLE
    // -------------------------------------------------------------------------

    void Awake()
    {
        timerScript = GetComponent<TimerScript>();
        if (PlayerPrefs.GetInt(FirstTimeLaunchKey, 0) == 0)
        {
            timerScript.isTimerRunning = false;
            Time.timeScale = 0;
            instructionsMenu.SetActive(true);
            PlayerPrefs.SetInt(FirstTimeLaunchKey, 1);
        }
        else
        {
            timerScript.isTimerRunning = true;
            instructionsMenu.SetActive(false);
        }
    }

    void Start()
    {
        Time.timeScale = 1;
        if (IsServer)
        {
            networkScoreEnemy.Value  = 0;
            networkScorePlayer.Value = 0;
            networkTimeLeft.Value    = 120f;
            networkPlayerTurn.Value  = true;
        }
        BoardScript.scoreEnemy  = 0;
        BoardScript.scorePlayer = 0;

        if (batchTransmitter != null)
            batchTransmitter.SetCarromGameManager(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
            StartCoroutine(SendInitialBoardStateDelayed());
    }

    private IEnumerator SendInitialBoardStateDelayed()
    {
        yield return null; // let PieceRegistry.Start() register pieces
        EndStatePayload payload = ConstructEndState();
        SyncInitialBoardStateClientRpc(payload);
        Debug.Log($"[CarromGameManager] Initial board state synced — {payload.pieceCount} pieces");
    }

    [ClientRpc]
    private void SyncInitialBoardStateClientRpc(EndStatePayload payload)
    {
        if (IsServer) return;
        playbackEngine?.ApplyEndState(payload);
        Debug.Log("[CarromGameManager] CLIENT: Initial board state applied");
    }

    void Update()
    {
        bool isPlayerTurnActive = IsSpawned
            ? (IsHost ? networkPlayerTurn.Value : !networkPlayerTurn.Value)
            : StrikerController.playerTurn;

        if (slider   != null) slider.SetActive(isPlayerTurnActive);
        if (turnText != null) turnText.SetActive(isPlayerTurnActive);

        int   currentScoreEnemy  = IsSpawned ? networkScoreEnemy.Value  : BoardScript.scoreEnemy;
        int   currentScorePlayer = IsSpawned ? networkScorePlayer.Value : BoardScript.scorePlayer;
        float currentTimeLeft    = IsSpawned ? networkTimeLeft.Value    : timerScript.timeLeft;

        if (currentScoreEnemy >= 8 || currentScorePlayer >= 8 || currentTimeLeft <= 0)
            if (!IsSpawned || IsServer) onGameOver();

        if (Input.GetKeyDown(KeyCode.Escape) && !gameOver)
        {
            if (isPaused) ResumeGame(); else PauseGame();
        }
    }

    private void LateUpdate()
    {
        if (gameOver) return;
        if (IsSpawned)
        {
            scoreTextEnemy.text  = networkScoreEnemy.Value.ToString();
            scoreTextPlayer.text = networkScorePlayer.Value.ToString();
        }
        else
        {
            scoreTextEnemy.text  = BoardScript.scoreEnemy.ToString();
            scoreTextPlayer.text = BoardScript.scorePlayer.ToString();
        }
    }

    // -------------------------------------------------------------------------
    // GAME OVER
    // -------------------------------------------------------------------------

    IEnumerator playAnimation() { animator.SetTrigger("fade"); yield return new WaitForSeconds(1f); }

    void onGameOver()
    {
        if (IsSpawned && !IsServer) return;

        int hostScore   = IsSpawned ? networkScorePlayer.Value : BoardScript.scorePlayer;
        int clientScore = IsSpawned ? networkScoreEnemy.Value  : BoardScript.scoreEnemy;

        GameResult result = hostScore > clientScore ? GameResult.HostWins
                          : clientScore > hostScore ? GameResult.ClientWins
                          : GameResult.Draw;

        if (IsSpawned)
        {
            ShowGameOverClientRpc(result);
        }
        else
        {
            gameOver = true;
            gameOverMenu.SetActive(true);
            Time.timeScale = 0;
            gameOverText.text = BoardScript.scoreEnemy > BoardScript.scorePlayer ? "You Lose!"
                              : BoardScript.scoreEnemy < BoardScript.scorePlayer ? "You Win!"
                              : "Draw!";
        }
    }

    [ClientRpc]
    private void ShowGameOverClientRpc(GameResult result)
    {
        gameOver = true;
        gameOverMenu.SetActive(true);
        Time.timeScale = 0;
        gameOverText.text = result == GameResult.HostWins   ? (IsHost ? "You Win!"  : "You Lose!")
                          : result == GameResult.ClientWins ? (IsHost ? "You Lose!" : "You Win!")
                          : "Draw!";
    }

    // -------------------------------------------------------------------------
    // MENUS
    // -------------------------------------------------------------------------

    public void ResumeGame()  { isPaused = false; pauseMenu.SetActive(false); Time.timeScale = 1; }
    public void PauseGame()   { isPaused = true;  pauseMenu.SetActive(true);  Time.timeScale = 0; }
    public void RestartGame() { SceneManager.LoadScene(1); }
    public void QuitGame()    { StartCoroutine(playAnimation()); SceneManager.LoadScene(0); }

    public void NextPage()
    {
        instructionsText.pageToDisplay++;
        if (instructionsText.pageToDisplay == 3)
        {
            Time.timeScale = 1;
            timerScript.isTimerRunning = true;
            instructionsMenu.SetActive(false);
        }
    }

    // -------------------------------------------------------------------------
    // PHYSICS QUERY
    // -------------------------------------------------------------------------

    public bool AreAllObjectsStopped()
    {
        string[] tags = { "Black", "White", "Queen", "Striker" };
        foreach (string tag in tags)
            foreach (GameObject go in GameObject.FindGameObjectsWithTag(tag))
            {
                Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
                if (rb != null && rb.linearVelocity.magnitude >= velocityThreshold) return false;
            }
        return true;
    }

    // -------------------------------------------------------------------------
    // SHOT LIFECYCLE — runs on the ACTIVE PLAYER (owner), not server-only
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called locally by the active player (Host OR Client) the moment they shoot.
    /// Starts telemetry recording on the machine that owns the physics.
    /// Notifies BatchTransmitter to freeze the spectator's pieces.
    /// </summary>
    public void OnShotStart()
    {
        if (!IsSpawned) return;

        Debug.Log($"[CarromGameManager] ===== SHOT START (IsServer={IsServer}) =====");

        telemetryRecorder?.StartRecording();

        // BatchTransmitter.NotifyShotStart must run on the server to send RPCs.
        // If we are the server (Host shooting), call directly.
        // If we are the client, ask the server to broadcast the freeze.
        if (IsServer)
            batchTransmitter?.NotifyShotStart();
        else
            batchTransmitter?.RequestShotStartServerRpc();
    }

    /// <summary>
    /// Called locally by the active player when all pieces have stopped.
    /// Stops recording, builds the end state, and transmits the replay to the peer.
    /// Turn flip happens ONLY inside TransferAuthority at the very end of the cycle.
    /// </summary>
    public void OnShotComplete()
    {
        if (!IsSpawned) return;

        Debug.Log($"[CarromGameManager] ===== SHOT COMPLETE (IsServer={IsServer}) =====");

        telemetryRecorder?.StopRecording();

        EndStatePayload endState = ConstructEndState();
        batchTransmitter?.TransmitFullReplay(endState);
    }

    // -------------------------------------------------------------------------
    // TURN & AUTHORITY
    // -------------------------------------------------------------------------

    /// <summary>
    /// Flips networkPlayerTurn. Only callable on server.
    /// Called inside TransferAuthority so the flip happens at the very end of the cycle.
    /// </summary>
    private void FlipTurn()
    {
        if (!IsServer) return;
        networkPlayerTurn.Value = !networkPlayerTurn.Value;
        UpdateTurnDisplayClientRpc(networkPlayerTurn.Value);
        Debug.Log($"[CarromGameManager] Turn flipped — networkPlayerTurn={networkPlayerTurn.Value}");
    }

    [ClientRpc]
    private void UpdateTurnDisplayClientRpc(bool isPlayerTurn)
    {
        Debug.Log($"[CarromGameManager] Turn display updated — {isPlayerTurn}");
    }

    public GameObject GetActiveStriker() => activeStriker;

    /// <summary>
    /// Returns the client ID of the player whose turn it currently is.
    /// Reads networkPlayerTurn which reflects the CURRENT (not next) turn.
    /// </summary>
    public ulong GetActivePlayerClientId()
    {
        if (!IsSpawned) return ulong.MaxValue;
        // GetCurrentActivePlayerId requires IsServer — call via server path only
        if (IsServer) return GetCurrentActivePlayerId();
        // On client, derive from networkPlayerTurn
        bool myTurn = !networkPlayerTurn.Value; // client's turn when networkPlayerTurn=false
        return myTurn ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
    }

    public void TriggerAuthorityTransfer() => TransferAuthority();

    private void TransferAuthority()
    {
        if (!IsSpawned || !IsServer) return;

        // Flip the turn NOW — at the very end of the cycle
        FlipTurn();

        ulong newActivePlayerId = GetCurrentActivePlayerId();
        if (newActivePlayerId == ulong.MaxValue)
        {
            Debug.LogWarning("[CarromGameManager] Could not determine next active player");
            return;
        }

        List<GameObject> pieceList = new List<GameObject>();
        pieceList.AddRange(GameObject.FindGameObjectsWithTag("Black"));
        pieceList.AddRange(GameObject.FindGameObjectsWithTag("White"));
        pieceList.AddRange(GameObject.FindGameObjectsWithTag("Queen"));
        pieceList.AddRange(GameObject.FindGameObjectsWithTag("Striker"));

        int transferred = 0;
        foreach (GameObject piece in pieceList)
        {
            NetworkObject no = piece.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) { no.ChangeOwnership(newActivePlayerId); transferred++; }
        }

        Debug.Log($"[CarromGameManager] Authority → client {newActivePlayerId} ({transferred} pieces)");
        NotifyAuthorityChangeClientRpc(newActivePlayerId);
    }

    [ClientRpc]
    private void NotifyAuthorityChangeClientRpc(ulong newActivePlayerId)
    {
        bool isNewActivePlayer = NetworkManager.Singleton.LocalClientId == newActivePlayerId;
        List<GameObject> pieceList = new List<GameObject>();
        pieceList.AddRange(GameObject.FindGameObjectsWithTag("Black"));
        pieceList.AddRange(GameObject.FindGameObjectsWithTag("White"));
        pieceList.AddRange(GameObject.FindGameObjectsWithTag("Queen"));
        pieceList.AddRange(GameObject.FindGameObjectsWithTag("Striker"));
        foreach (GameObject piece in pieceList)
            piece.GetComponent<NetworkPhysicsObject>()?.SetAuthority(isNewActivePlayer);
    }

    private ulong GetCurrentActivePlayerId()
    {
        if (!IsServer) return ulong.MaxValue;
        bool currentTurnIsHost = networkPlayerTurn.Value;
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            bool isHostClient = clientId == NetworkManager.Singleton.LocalClientId;
            if (currentTurnIsHost && isHostClient)  return clientId;
            if (!currentTurnIsHost && !isHostClient) return clientId;
        }
        return ulong.MaxValue;
    }

    // -------------------------------------------------------------------------
    // END STATE
    // -------------------------------------------------------------------------

    public EndStatePayload ConstructEndState()
    {
        EndStatePayload payload = new EndStatePayload(maxPieces: 20);
        if (pieceRegistry == null) { Debug.LogError("[CarromGameManager] PieceRegistry is null"); return payload; }

        for (byte id = 0; id < 20; id++)
        {
            GameObject piece = pieceRegistry.GetPiece(id);
            if (piece == null) continue;
            payload.finalStates[payload.pieceCount++] = new PieceState
            {
                pieceId   = id,
                xPosition = piece.transform.position.x,
                yPosition = piece.transform.position.y,
                zRotation = piece.transform.eulerAngles.z
            };
        }
        Debug.Log($"[CarromGameManager] End state built — {payload.pieceCount} pieces");
        return payload;
    }

    // Single-player legacy
    public void SwitchTurn()
    {
        if (!IsSpawned) StrikerController.playerTurn = !StrikerController.playerTurn;
    }
}
