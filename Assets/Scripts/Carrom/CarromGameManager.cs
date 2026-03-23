using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Collections;
using Unity.Services.Authentication;

public enum GameResult { HostWins, ClientWins, Draw }

/// <summary>
/// Identifies the four piece types for the Shot Ledger.
/// Accessible globally so BoardScript and future rule modules can reference it.
/// </summary>
public enum CoinType { White, Black, Queen, Striker }

/// <summary>
/// Selectable game modes. Drives scoring rules and win threshold.
/// </summary>
public enum GameMode { Freestyle, Classic }

public class CarromGameManager : NetworkBehaviour
{
    /// <summary>
    /// Static carrier — set by CharacterSelectionManager before scene load,
    /// read by Start() to configure the match rules.
    /// </summary>
    public static GameMode ActiveRuleset = GameMode.Freestyle;
    public NetworkVariable<int>   networkScorePlayer      = new NetworkVariable<int>(0,     NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int>   networkScoreEnemy       = new NetworkVariable<int>(0,     NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> networkTimeLeft         = new NetworkVariable<float>(120f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool>  networkPlayerTurn       = new NetworkVariable<bool>(true,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int>   networkCoinsRemaining   = new NetworkVariable<int>(19,     NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool>  hostSecuredQueen        = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool>  clientSecuredQueen      = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString64Bytes> networkHostName   = new NetworkVariable<FixedString64Bytes>("",          NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString64Bytes> networkClientName = new NetworkVariable<FixedString64Bytes>("Waiting...", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    bool gameOver = false;
    bool isPaused = false;

    [SerializeField] TextMeshProUGUI scoreTextEnemy;
    [SerializeField] TextMeshProUGUI scoreTextPlayer;
    [SerializeField] TextMeshProUGUI hostNameText;
    [SerializeField] TextMeshProUGUI clientNameText;
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

    [Header("Game Rules")]
    [SerializeField] private int       winScoreThreshold    = 160;
    [SerializeField] private GameMode  currentGameMode      = GameMode.Freestyle;
    [SerializeField] private float     spawnClearanceRadius = 0.5f;
    [SerializeField] private LayerMask pieceLayerMask;

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
            currentGameMode               = ActiveRuleset; // read static handoff from CharacterSelection
            networkScoreEnemy.Value       = 0;
            networkScorePlayer.Value      = 0;
            networkTimeLeft.Value         = 120f;
            networkPlayerTurn.Value       = true;
            networkCoinsRemaining.Value   = 19;
            hostSecuredQueen.Value        = false;
            clientSecuredQueen.Value      = false;

            // Classic still uses a threshold; Freestyle uses coin depletion instead
            winScoreThreshold = currentGameMode == GameMode.Classic ? 9 : int.MaxValue;
            Debug.Log($"[CarromGameManager] Mode={currentGameMode}, winThreshold={winScoreThreshold}");
        }

        if (batchTransmitter != null)
            batchTransmitter.SetCarromGameManager(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        UnityEngine.UI.Slider sliderComponent = slider != null
            ? slider.GetComponent<UnityEngine.UI.Slider>()
            : null;

        if (IsServer)
        {
            // Host: enforce default orientation (safe on scene reload)
            Camera.main.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            if (sliderComponent != null)
                sliderComponent.direction = UnityEngine.UI.Slider.Direction.LeftToRight;

            StartCoroutine(SendInitialBoardStateDelayed());
        }
        else
        {
            // Client: flip camera 180° on Z so the board reads naturally from their side
            Camera.main.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
            // Mirror the slider so dragging right still moves the striker right visually
            if (sliderComponent != null)
                sliderComponent.direction = UnityEngine.UI.Slider.Direction.RightToLeft;
        }

        // ---- Identity Handshake ----
        string localName = AuthenticationService.Instance.PlayerName
                        ?? AuthenticationService.Instance.PlayerId
                        ?? "Player";

        if (IsServer)
        {
            networkHostName.Value = new FixedString64Bytes(localName);
        }
        else
        {
            SubmitClientNameServerRpc(localName);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitClientNameServerRpc(string clientName)
    {
        networkClientName.Value = new FixedString64Bytes(clientName);
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

        int   currentScoreEnemy  = networkScoreEnemy.Value;
        int   currentScorePlayer = networkScorePlayer.Value;
        float currentTimeLeft    = IsSpawned ? networkTimeLeft.Value : timerScript.timeLeft;

        // GUARD: Only check win conditions if the game is actually running
        if (!gameOver)
        {
            bool boardCleared = currentGameMode == GameMode.Freestyle && networkCoinsRemaining.Value <= 0;
            if (boardCleared || (currentGameMode == GameMode.Classic && (currentScoreEnemy >= winScoreThreshold || currentScorePlayer >= winScoreThreshold)) || currentTimeLeft <= 0)
                if (!IsSpawned || IsServer) onGameOver();
        }

        if (Input.GetKeyDown(KeyCode.Escape) && !gameOver)
        {
            if (isPaused) ResumeGame(); else PauseGame();
        }
    }

    private void LateUpdate()
    {
        if (gameOver) return;
        scoreTextEnemy.text  = networkScoreEnemy.Value.ToString();
        scoreTextPlayer.text = networkScorePlayer.Value.ToString();
        if (hostNameText   != null) hostNameText.text   = GetCleanPlayerName(networkHostName.Value.ToString());
        if (clientNameText != null) clientNameText.text = GetCleanPlayerName(networkClientName.Value.ToString());
    }

    // -------------------------------------------------------------------------
    // GAME OVER
    // -------------------------------------------------------------------------

    IEnumerator playAnimation() { animator.SetTrigger("fade"); yield return new WaitForSeconds(1f); }

    void onGameOver()
    {
        if (IsSpawned && !IsServer) return;

        gameOver = true; // Ensure the Server locally knows the game is over immediately

        int hostScore   = networkScorePlayer.Value;
        int clientScore = networkScoreEnemy.Value;

        GameResult result;
        if      (hostScore > clientScore)       result = GameResult.HostWins;
        else if (clientScore > hostScore)       result = GameResult.ClientWins;
        else if (hostSecuredQueen.Value)        result = GameResult.HostWins;   // tiebreaker
        else if (clientSecuredQueen.Value)      result = GameResult.ClientWins; // tiebreaker
        else                                    result = GameResult.Draw;       // time-out, no queen

        if (IsSpawned)
        {
            ShowGameOverClientRpc(result);
        }
        else
        {
            gameOver = true;
            gameOverMenu.SetActive(true);
            Time.timeScale = 0;
            gameOverText.text = networkScoreEnemy.Value > networkScorePlayer.Value ? "You Lose!"
                              : networkScoreEnemy.Value < networkScorePlayer.Value ? "You Win!"
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

    [ServerRpc(RequireOwnership = false)]
    private void RequestRestartServerRpc()
    {
        Debug.Log("[CarromGameManager] Server received restart request. Unfreezing all clients...");
        UnfreezeAllClientRpc();
        LoadingSceneManager.Instance.LoadScene(SceneName.CharacterSelection);
    }

    [ClientRpc]
    private void UnfreezeAllClientRpc()
    {
        Time.timeScale = 1;
        Debug.Log("[CarromGameManager] Client unfrozen for scene transition.");
    }

    public void RestartGame()
    {
        Debug.Log("[CarromGameManager] Restart button clicked locally!");
        Time.timeScale = 1;
        RequestRestartServerRpc();
    }

    public void QuitGame()
    {
        Time.timeScale = 1;
        StartCoroutine(playAnimation());
        if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(0);
    }

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

        batchTransmitter?.StartShotAsActivePlayer();
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

    // -------------------------------------------------------------------------
    // SHOT LEDGER — server-authoritative, cleared after every TransferAuthority
    // -------------------------------------------------------------------------

    // Transient record of every coin pocketed during the current physical shot.
    private readonly List<CoinType> shotLedger = new List<CoinType>();

    // True when the Queen was pocketed last shot without a cover — next shot must cover it.
    private bool isQueenPending = false;

    /// <summary>
    /// Called by BoardScript (via ServerRpc) each time a coin enters a pocket.
    /// Server-only — the ledger is authoritative.
    /// </summary>
    public void ReportPocketedCoin(CoinType coinType)
    {
        if (!IsServer) return;
        shotLedger.Add(coinType);
        Debug.Log($"[CarromGameManager] Ledger: {coinType} pocketed (total this shot: {shotLedger.Count})");
    }

    /// <summary>
    /// Routes to the correct mode evaluator. Returns true if the active player retains their turn.
    /// Called inside TransferAuthority before shotLedger.Clear().
    /// </summary>
    private bool EvaluateShotLedger()
    {
        return currentGameMode == GameMode.Classic
            ? EvaluateClassicMode()
            : EvaluateFreestyleMode();
    }

    /// <summary>
    /// Freestyle: all coins score for the active player regardless of color.
    /// Turn retained if any scoring coin (White/Black/Queen) was pocketed legally.
    /// Enforces: Striker foul economy, Queen Last rule.
    /// </summary>
    private bool EvaluateFreestyleMode()
    {
        bool isHostTurn  = networkPlayerTurn.Value;
        int  points      = 0;
        bool retainTurn  = false;
        bool queenSecured = hostSecuredQueen.Value || clientSecuredQueen.Value;
        bool queenPocketedThisShot = shotLedger.Contains(CoinType.Queen);

        foreach (CoinType coin in shotLedger)
        {
            if (coin == CoinType.White || coin == CoinType.Black)
            {
                // --- Queen Last Rule ---
                // If this was the last coin of its color on the board AND the Queen
                // hasn't been secured AND the Queen wasn't pocketed this same shot,
                // the shot is illegal — return the coin and deny turn retention.
                if (GetBoardCoinCount(coin) == 0 && !queenSecured && !queenPocketedThisShot && !isQueenPending)
                {
                    byte illegalId = GetGraveyardCoinId(coin);
                    if (illegalId != 255) RespawnPiece(illegalId);
                    Debug.Log($"[CGM] Freestyle: Queen Last rule triggered for {coin} — coin returned");
                    continue; // no points, no retain for this coin
                }

                points += coin == CoinType.White ? 20 : 10;
                networkCoinsRemaining.Value -= 1;
                retainTurn = true;
            }
            else if (coin == CoinType.Striker)
            {
                // --- Zero-Floor Policy ---
                // Active player's running score = their NetworkVariable + points accumulated so far this shot.
                int currentScore = (isHostTurn ? networkScorePlayer.Value : networkScoreEnemy.Value) + points;

                if (currentScore <= 0)
                {
                    // Rule 1: Score is already at zero — waive the penalty entirely.
                    // No deduction, no coin return. Turn is simply lost (retainTurn stays false).
                    Debug.Log("[CGM] Freestyle: Striker foul — score at zero, penalty waived");
                }
                else
                {
                    // Rule 2: Minimum Denomination Return.
                    // Return the lowest-value coin the player can logically own.
                    // Priority: Black (10 pts) first, then White (20 pts).
                    // Guard: only pull a White if the player's score is >= 20 (they couldn't own one otherwise).
                    byte penaltyId   = 255;
                    int  penaltyPts  = 0;

                    byte blackId = GetGraveyardCoinId(CoinType.Black);
                    byte whiteId = GetGraveyardCoinId(CoinType.White);

                    if (blackId != 255 && currentScore >= 10)
                    {
                        penaltyId  = blackId;
                        penaltyPts = 10;
                    }
                    else if (whiteId != 255 && currentScore >= 20)
                    {
                        penaltyId  = whiteId;
                        penaltyPts = 20;
                    }

                    if (penaltyId != 255)
                    {
                        points -= penaltyPts;
                        RespawnPiece(penaltyId);
                        networkCoinsRemaining.Value += 1;
                        Debug.Log($"[CGM] Freestyle: Striker foul — piece {penaltyId} ({penaltyPts} pts) returned to board");
                    }
                    else
                    {
                        // No returnable coin found (graveyard empty) — deduct points only, floor at zero.
                        int deduction = Mathf.Min(10, currentScore);
                        points -= deduction;
                        Debug.Log($"[CGM] Freestyle: Striker foul — no coin to return, deducted {deduction} pts");
                    }
                }
            }
        }

        // --- Queen state machine ---
        bool hasCover = shotLedger.Contains(CoinType.White) || shotLedger.Contains(CoinType.Black);

        if (isQueenPending)
        {
            if (hasCover)
            {
                points += 50;
                networkCoinsRemaining.Value -= 1;
                if (isHostTurn) hostSecuredQueen.Value = true; else clientSecuredQueen.Value = true;
                isQueenPending = false;
                retainTurn = true;
                Debug.Log("[CGM] Queen covered (follow-up) — +50");
            }
            else { isQueenPending = false; RespawnPiece(19); Debug.Log("[CGM] Queen cover failed — respawning"); }
        }
        else if (queenPocketedThisShot)
        {
            retainTurn = true;
            if (hasCover)
            {
                points += 50;
                networkCoinsRemaining.Value -= 1;
                if (isHostTurn) hostSecuredQueen.Value = true; else clientSecuredQueen.Value = true;
                Debug.Log("[CGM] Queen same-shot cover — +50");
            }
            else { isQueenPending = true; Debug.Log("[CGM] Queen pending cover next shot"); }
        }

        if (points != 0)
        {
            if (isHostTurn) networkScorePlayer.Value += points;
            else            networkScoreEnemy.Value  += points;
            Debug.Log($"[CGM] Freestyle: {points} pts → {(isHostTurn ? "Host" : "Client")} | coinsLeft={networkCoinsRemaining.Value}");
        }

        // --- Ultimate Foul Override (The Guillotine Clause) ---
        // Regardless of score, penalties waived, or coins pocketed, a Striker foul forcibly ends the turn.
        if (shotLedger.Contains(CoinType.Striker))
        {
            retainTurn = false;
            Debug.Log("[CGM] Freestyle: Striker foul absolute override — turn forcibly lost");
        }

        return retainTurn;
    }

    /// <summary>
    /// Classic: strict color assignment, absolute scoring, strict Queen cover.
    /// Host pockets White, Client pockets Black — pocketing the opponent's coin scores for them.
    /// Turn retained only if the active player pocketed their own coin or the Queen legally.
    /// Enforces: Striker foul economy, Queen Last rule.
    /// </summary>
    private bool EvaluateClassicMode()
    {
        bool     isHostTurn = networkPlayerTurn.Value;
        CoinType myCoin     = isHostTurn ? CoinType.White : CoinType.Black;
        bool     retainTurn = false;
        bool     queenSecured = hostSecuredQueen.Value || clientSecuredQueen.Value;
        bool     queenPocketedThisShot = shotLedger.Contains(CoinType.Queen);

        // --- Absolute scoring with Queen Last enforcement ---
        foreach (CoinType coin in shotLedger)
        {
            if (coin == CoinType.White || coin == CoinType.Black)
            {
                // Queen Last Rule: illegal to clear ANY color's last coin before the Queen is secured
                // Exception: If the Queen was pocketed this shot OR is currently pending a cover shot, clearing the last coin is a legal cover.
                if ((coin == CoinType.White || coin == CoinType.Black) && GetBoardCoinCount(coin) == 0 && !queenSecured && !queenPocketedThisShot && !isQueenPending)
                {
                    byte illegalId = GetGraveyardCoinId(coin);
                    if (illegalId != 255) RespawnPiece(illegalId);
                    Debug.Log($"[CGM] Classic: Queen Last rule triggered for {coin} — coin returned");
                    // Deny turn retention if the active player illegally cleared their own color
                    if (coin == myCoin) retainTurn = false;
                    continue; // no score, no retain for this coin
                }

                // Absolute scoring: coin always scores for its color's owner
                if (coin == CoinType.White) networkScorePlayer.Value += 1;
                else                        networkScoreEnemy.Value  += 1;

                if (coin == myCoin) retainTurn = true;
            }
            else if (coin == CoinType.Striker)
            {
                // --- Zero-Floor Policy ---
                int currentScore = isHostTurn ? networkScorePlayer.Value : networkScoreEnemy.Value;

                if (currentScore <= 0)
                {
                    // Rule 1: Score already at zero — waive penalty entirely.
                    // No deduction, no coin return. Turn is simply lost.
                    Debug.Log("[CGM] Classic: Striker foul — score at zero, penalty waived");
                }
                else
                {
                    // Rule 2: Strict color return — deduct 1 pt and return one owned coin.
                    if (isHostTurn) networkScorePlayer.Value -= 1;
                    else            networkScoreEnemy.Value  -= 1;

                    byte penaltyId = GetGraveyardCoinId(myCoin);
                    if (penaltyId != 255)
                    {
                        RespawnPiece(penaltyId);
                        Debug.Log($"[CGM] Classic: Striker foul — piece {penaltyId} returned to board");
                    }
                    else
                    {
                        Debug.Log("[CGM] Classic: Striker foul — no coin to return, points deducted only");
                    }
                }
            }
        }

        // --- Queen state machine (strict cover) ---
        bool hasStrictCover = shotLedger.Contains(myCoin);

        if (isQueenPending)
        {
            if (hasStrictCover)
            {
                if (isHostTurn) hostSecuredQueen.Value   = true;
                else            clientSecuredQueen.Value = true;
                isQueenPending = false;
                retainTurn = true;
                Debug.Log("[CGM] Classic: Queen covered (follow-up) — Queen secured (0 pts)");
            }
            else
            {
                isQueenPending = false;
                RespawnPiece(19);
                Debug.Log("[CGM] Classic: Queen cover failed — respawning");
            }
        }
        else if (queenPocketedThisShot)
        {
            retainTurn = true;
            if (hasStrictCover)
            {
                if (isHostTurn) hostSecuredQueen.Value   = true;
                else            clientSecuredQueen.Value = true;
                Debug.Log("[CGM] Classic: Queen same-shot cover — Queen secured (0 pts)");
            }
            else
            {
                isQueenPending = true;
                Debug.Log("[CGM] Classic: Queen pending strict cover next shot");
            }
        }

        // --- Ultimate Foul Override (The Guillotine Clause) ---
        // Regardless of score, penalties waived, or Queen covers, a Striker foul forcibly ends the turn.
        if (shotLedger.Contains(CoinType.Striker))
        {
            retainTurn = false;
            Debug.Log("[CGM] Classic: Striker foul absolute override — turn forcibly lost");
        }

        return retainTurn;
    }

    /// <summary>
    /// Server-side trigger: finds a safe spawn position then broadcasts the respawn to all clients.
    /// Used for Queen cover failures and Striker foul penalty returns.
    /// </summary>
    private void RespawnPiece(byte pieceId)
    {
        if (!IsServer) return;
        Vector3 safePos = FindSafeRespawnPosition();
        Debug.Log($"[CarromGameManager] Broadcasting respawn for piece {pieceId} → {safePos}");
        RespawnPieceClientRpc(pieceId, safePos);
    }

    /// <summary>
    /// Executes on every client (including host).
    /// Snaps the piece to the server-calculated safe position, zeroes velocity, re-enables sprite,
    /// and patches the pending EndStatePayload so ApplyEndState() doesn't re-pocket it.
    /// </summary>
    [ClientRpc]
    private void RespawnPieceClientRpc(byte pieceId, Vector3 spawnPosition)
    {
        GameObject piece = pieceRegistry?.GetPiece(pieceId);
        if (piece == null)
        {
            Debug.LogWarning($"[CarromGameManager] RespawnPieceClientRpc: piece {pieceId} not found");
            return;
        }

        piece.transform.position = spawnPosition;

        Rigidbody2D rb = piece.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }

        SpriteRenderer sr = piece.GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = true;

        playbackEngine?.PatchEndState(pieceId, spawnPosition);

        Debug.Log($"[CarromGameManager] Piece {pieceId} respawned at {spawnPosition} (all clients)");
    }

    // -------------------------------------------------------------------------
    // SAFE RESPAWN SPATIAL SEARCH — Physics2D BVH spiral, piece layer only
    // -------------------------------------------------------------------------

    /// <summary>
    /// Walks a dense Archimedean spiral outward from center, using Physics2D.OverlapCircle
    /// against pieceLayerMask to find the tightest available gap on the physical board.
    /// Check radius is 90% of spawnClearanceRadius to allow snug placement without rejection.
    /// </summary>
    private Vector3 FindSafeRespawnPosition()
    {
        float checkRadius = spawnClearanceRadius * 0.9f;

        // Fast path — center is clear
        if (Physics2D.OverlapCircle(Vector2.zero, checkRadius, pieceLayerMask) == null)
            return Vector3.zero;

        const int   maxAttempts  = 300;
        const float angleStep    = 15f * Mathf.Deg2Rad;
        const float radiusGrowth = 0.05f;

        float angle = 0f;
        for (int i = 0; i < maxAttempts; i++)
        {
            angle += angleStep;
            float    radius    = angle * radiusGrowth;
            Vector2  candidate = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

            if (Physics2D.OverlapCircle(candidate, checkRadius, pieceLayerMask) == null)
            {
                Debug.Log($"[CarromGameManager] Safe respawn found at attempt {i}: {candidate}");
                return new Vector3(candidate.x, candidate.y, 0f);
            }
        }

        Debug.LogWarning("[CarromGameManager] FindSafeRespawnPosition: board too crowded, defaulting to zero");
        return Vector3.zero;
    }

    // -------------------------------------------------------------------------
    // BOARD STATE HELPERS — server-authoritative queries via PieceRegistry
    // White IDs: 1-9 | Black IDs: 10-18 | Queen ID: 19 | Striker ID: 0
    // Graveyard: x >= 900f
    // -------------------------------------------------------------------------

    /// <summary>Returns the ID of the first coin of the given color found in the graveyard, or 255 if none.</summary>
    private byte GetGraveyardCoinId(CoinType color)
    {
        if (pieceRegistry == null) return 255;
        byte startId = color == CoinType.White ? (byte)1  : (byte)10;
        byte endId   = color == CoinType.White ? (byte)9  : (byte)18;
        for (byte id = startId; id <= endId; id++)
        {
            GameObject piece = pieceRegistry.GetPiece(id);
            if (piece != null && piece.transform.position.x >= 900f)
                return id;
        }
        return 255;
    }

    /// <summary>Returns the count of pieces of the given color currently on the board (x &lt; 900).</summary>
    private int GetBoardCoinCount(CoinType color)
    {
        if (pieceRegistry == null) return 0;
        byte startId = color == CoinType.White ? (byte)1  : (byte)10;
        byte endId   = color == CoinType.White ? (byte)9  : (byte)18;
        int  count   = 0;
        for (byte id = startId; id <= endId; id++)
        {
            GameObject piece = pieceRegistry.GetPiece(id);
            if (piece != null && piece.transform.position.x < 900f)
                count++;
        }
        return count;
    }

    private void TransferAuthority()
    {
        if (!IsSpawned || !IsServer) return;

        // ---- Shot Ledger Evaluation (Turn Interceptor) ----
        // Delegate entirely to the mode evaluator — it scores AND returns the retain decision.
        bool retainTurn = EvaluateShotLedger();
        Debug.Log($"[CarromGameManager] Ledger evaluated — retainTurn={retainTurn}, mode={currentGameMode}");
        shotLedger.Clear();

        if (retainTurn)
        {
            // Active player keeps the turn — reset the striker in place without ownership transfer.
            // ChangeOwnership(sameId) does NOT re-fire OnGainedOwnership, so we use an explicit RPC.
            StrikerController striker = activeStriker != null
                ? activeStriker.GetComponent<StrikerController>()
                : null;

            if (striker != null)
                striker.RetainTurnResetClientRpc();
            else
                Debug.LogWarning("[CarromGameManager] Could not find StrikerController for turn retention reset");

            Debug.Log("[CarromGameManager] Turn retained — striker reset via RetainTurnResetClientRpc");
            return;
        }

        // ---- Normal turn pass ----
        FlipTurn();

        ulong newActivePlayerId = GetCurrentActivePlayerId();
        if (newActivePlayerId == ulong.MaxValue)
        {
            Debug.LogWarning("[CarromGameManager] Could not determine next active player");
            return;
        }

        List<GameObject> allPieces = new List<GameObject>();
        allPieces.AddRange(GameObject.FindGameObjectsWithTag("Black"));
        allPieces.AddRange(GameObject.FindGameObjectsWithTag("White"));
        allPieces.AddRange(GameObject.FindGameObjectsWithTag("Queen"));
        allPieces.AddRange(GameObject.FindGameObjectsWithTag("Striker"));

        int transferred = 0;
        foreach (GameObject piece in allPieces)
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

    /// <summary>
    /// Strips the UGS unique identifier suffix (e.g., "#1234") for clean UI display.
    /// Does not alter the underlying NetworkVariable data.
    /// </summary>
    private string GetCleanPlayerName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return rawName;
        int hashIndex = rawName.LastIndexOf('#');
        if (hashIndex > 0) return rawName.Substring(0, hashIndex);
        return rawName;
    }
}
