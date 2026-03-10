using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.Netcode;

// Enum for game over results
public enum GameResult
{
    HostWins,
    ClientWins,
    Draw
}

public class CarromGameManager : NetworkBehaviour
{
    // Network variables for multiplayer synchronization
    public NetworkVariable<int> networkScorePlayer = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> networkScoreEnemy = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> networkTimeLeft = new NetworkVariable<float>(120f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> networkPlayerTurn = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    bool gameOver = false;
    bool isPaused = false;

    // TextMeshProUGUI variables for displaying scores, game over text, and instructions.
    [SerializeField]
    TextMeshProUGUI scoreTextEnemy;

    [SerializeField]
    TextMeshProUGUI scoreTextPlayer;

    [SerializeField]
    TextMeshProUGUI gameOverText;

    [SerializeField]
    TextMeshProUGUI instructionsText;

    // Game object variables for menus, strikers, turn text, and a slider.
    [SerializeField]
    GameObject instructionsMenu;

    [SerializeField]
    GameObject pauseMenu;

    [SerializeField]
    GameObject gameOverMenu;

    [SerializeField]
    GameObject playerStriker;

    [SerializeField]
    GameObject enemyStriker;

    [SerializeField]
    GameObject turnText;

    [SerializeField]
    GameObject slider;

    [SerializeField]
    Animator animator;

    TimerScript timerScript;

    private const string FirstTimeLaunchKey = "FirstTimeLaunch";

    void Awake()
    {
        timerScript = GetComponent<TimerScript>();

        // Check if it's the first time launching the game.
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
        
        // Initialize scores
        if (IsServer)
        {
            networkScoreEnemy.Value = 0;
            networkScorePlayer.Value = 0;
            networkTimeLeft.Value = 120f;
            networkPlayerTurn.Value = true; // Host takes first turn
        }
        
        // Also initialize static fields for backward compatibility
        BoardScript.scoreEnemy = 0;
        BoardScript.scorePlayer = 0;
    }

    void Update()
    {
        // Determine current turn state
        bool isPlayerTurnActive;
        if (IsSpawned)
        {
            // In multiplayer, use network variable
            // Host's turn = networkPlayerTurn is true
            // Client's turn = networkPlayerTurn is false
            isPlayerTurnActive = IsHost ? networkPlayerTurn.Value : !networkPlayerTurn.Value;
            
            // In multiplayer, playerStriker is the local player's striker
            // enemyStriker is the opponent's striker
            // Host controls playerStriker (HostStriker), Client controls enemyStriker (ClientStriker)
            GameObject myStriker = IsHost ? playerStriker : enemyStriker;
            GameObject opponentStriker = IsHost ? enemyStriker : playerStriker;
            
            // Update UI based on turn state
            if (isPlayerTurnActive)
            {
                if (slider != null) slider.SetActive(true);
                if (turnText != null) turnText.SetActive(true);
                if (myStriker != null) myStriker.SetActive(true);
                if (opponentStriker != null) opponentStriker.SetActive(false);
            }
            else
            {
                if (slider != null) slider.SetActive(false);
                if (turnText != null) turnText.SetActive(false);
                if (myStriker != null) myStriker.SetActive(false);
                if (opponentStriker != null) opponentStriker.SetActive(true);
            }
        }
        else
        {
            // Single-player mode
            isPlayerTurnActive = StrikerController.playerTurn;
            
            if (isPlayerTurnActive)
            {
                if (slider != null) slider.SetActive(true);
                if (turnText != null) turnText.SetActive(true);
                if (playerStriker != null) playerStriker.SetActive(true);
                if (enemyStriker != null) enemyStriker.SetActive(false);
            }
            else
            {
                if (slider != null) slider.SetActive(false);
                if (turnText != null) turnText.SetActive(false);
                if (playerStriker != null) playerStriker.SetActive(false);
                if (enemyStriker != null) enemyStriker.SetActive(true);
            }
        }

        // Check game over conditions
        int currentScoreEnemy = IsSpawned ? networkScoreEnemy.Value : BoardScript.scoreEnemy;
        int currentScorePlayer = IsSpawned ? networkScorePlayer.Value : BoardScript.scorePlayer;
        float currentTimeLeft = IsSpawned ? networkTimeLeft.Value : timerScript.timeLeft;
        
        if (currentScoreEnemy >= 8 || currentScorePlayer >= 8 || currentTimeLeft <= 0)
        {
            // Only Host triggers game over in multiplayer
            if (!IsSpawned || IsServer)
            {
                onGameOver();
            }
        }

        if (Input.GetKeyDown(KeyCode.Escape) && !gameOver)
        {
            if (isPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }
    }

    private void LateUpdate()
    {
        if (!gameOver)
        {
            // Read from network variables if in multiplayer, otherwise use static fields
            if (IsSpawned)
            {
                scoreTextEnemy.text = networkScoreEnemy.Value.ToString();
                scoreTextPlayer.text = networkScorePlayer.Value.ToString();
            }
            else
            {
                scoreTextEnemy.text = BoardScript.scoreEnemy.ToString();
                scoreTextPlayer.text = BoardScript.scorePlayer.ToString();
            }
        }
    }

    IEnumerator playAnimation()
    {
        animator.SetTrigger("fade");
        yield return new WaitForSeconds(1f);
    }

    void onGameOver()
    {
        // In multiplayer, only Host triggers game over
        if (IsSpawned && !IsServer)
        {
            return;
        }
        
        // Calculate game result
        GameResult result;
        int hostScore = IsSpawned ? networkScorePlayer.Value : BoardScript.scorePlayer;
        int clientScore = IsSpawned ? networkScoreEnemy.Value : BoardScript.scoreEnemy;
        
        if (hostScore > clientScore)
        {
            result = GameResult.HostWins;
        }
        else if (clientScore > hostScore)
        {
            result = GameResult.ClientWins;
        }
        else
        {
            result = GameResult.Draw;
        }
        
        // In multiplayer, broadcast to all clients
        if (IsSpawned)
        {
            ShowGameOverClientRpc(result);
        }
        else
        {
            // Single-player fallback
            gameOver = true;
            gameOverMenu.SetActive(true);
            Time.timeScale = 0;
            if (BoardScript.scoreEnemy > BoardScript.scorePlayer)
            {
                gameOverText.text = "You Lose!";
            }
            else if (BoardScript.scoreEnemy < BoardScript.scorePlayer)
            {
                gameOverText.text = "You Win!";
            }
            else
            {
                gameOverText.text = "Draw!";
            }
        }
    }

    public void ResumeGame()
    {
        isPaused = false;
        pauseMenu.SetActive(false);
        Time.timeScale = 1;
    }

    public void PauseGame()
    {
        isPaused = true;
        pauseMenu.SetActive(true);
        Time.timeScale = 0;
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(1);
    }

    public void QuitGame()
    {
        StartCoroutine(playAnimation());
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
    [ClientRpc]
    private void ShowGameOverClientRpc(GameResult result)
    {
        gameOver = true;
        gameOverMenu.SetActive(true);
        Time.timeScale = 0;

        switch (result)
        {
            case GameResult.HostWins:
                gameOverText.text = IsHost ? "You Win!" : "You Lose!";
                break;
            case GameResult.ClientWins:
                gameOverText.text = IsHost ? "You Lose!" : "You Win!";
                break;
            case GameResult.Draw:
                gameOverText.text = "Draw!";
                break;
        }

        Debug.Log($"[Network] Game Over - Result: {result}");
    }

    [ClientRpc]
    private void UpdateTurnDisplayClientRpc(bool isPlayerTurn)
    {
        Debug.Log($"[Network] Turn updated - Player turn: {isPlayerTurn}");
        // Turn display will be updated in Update() based on networkPlayerTurn
    }
    // Check if all game objects have stopped moving
    public bool AreAllObjectsStopped()
    {
        GameObject[] coins = GameObject.FindGameObjectsWithTag("Black");
        GameObject[] whiteCoins = GameObject.FindGameObjectsWithTag("White");
        GameObject[] queens = GameObject.FindGameObjectsWithTag("Queen");
        GameObject[] strikers = GameObject.FindGameObjectsWithTag("Striker");

        // Check all coins
        foreach (GameObject coin in coins)
        {
            Rigidbody2D rb = coin.GetComponent<Rigidbody2D>();
            if (rb != null && rb.linearVelocity.magnitude >= 0.1f)
            {
                return false;
            }
        }

        foreach (GameObject coin in whiteCoins)
        {
            Rigidbody2D rb = coin.GetComponent<Rigidbody2D>();
            if (rb != null && rb.linearVelocity.magnitude >= 0.1f)
            {
                return false;
            }
        }

        foreach (GameObject queen in queens)
        {
            Rigidbody2D rb = queen.GetComponent<Rigidbody2D>();
            if (rb != null && rb.linearVelocity.magnitude >= 0.1f)
            {
                return false;
            }
        }

        foreach (GameObject striker in strikers)
        {
            Rigidbody2D rb = striker.GetComponent<Rigidbody2D>();
            if (rb != null && rb.linearVelocity.magnitude >= 0.1f)
            {
                return false;
            }
        }

        return true;
    }

    // Switch turn to the other player
    public void SwitchTurn()
    {
        if (IsSpawned && IsServer)
        {
            networkPlayerTurn.Value = !networkPlayerTurn.Value;
            UpdateTurnDisplayClientRpc(networkPlayerTurn.Value);
            Debug.Log($"[Network] Turn switched - Player turn: {networkPlayerTurn.Value}");
        }
        else if (!IsSpawned)
        {
            // Single-player mode
            StrikerController.playerTurn = !StrikerController.playerTurn;
        }
    }
    
    // Helper methods to get striker references
    public GameObject GetPlayerStriker()
    {
        return playerStriker;
    }
    
    public GameObject GetEnemyStriker()
    {
        return enemyStriker;
    }


}
