using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyCreateUI : MonoBehaviour {


    public static LobbyCreateUI Instance { get; private set; }


    [SerializeField] private Button createButton;
    [SerializeField] private Button lobbyNameButton;
    [SerializeField] private Button publicPrivateButton;
    [SerializeField] private Button maxPlayersButton;
    [SerializeField] private Button gameModeButton;
    [SerializeField] private TextMeshProUGUI lobbyNameText;
    [SerializeField] private TextMeshProUGUI publicPrivateText;
    [SerializeField] private TextMeshProUGUI maxPlayersText;
    [SerializeField] private TextMeshProUGUI gameModeText;
    [SerializeField] private Button joinLobbyButton;
    [SerializeField] private TMP_InputField joinLobbyCode;
    [SerializeField] private Button createPrivateRoomButton;
    [SerializeField] private Button createPublicRoomButton;
    private string lobbyName;
    private string joinCode;
    private bool isPrivate;
    private int maxPlayers;
    private LobbyManager.GameMode gameMode;

    private void Awake() {
        Instance = this;

        createButton.onClick.AddListener(() => {
            // LobbyManager.Instance.CreateLobby(
            //     lobbyName,
            //     maxPlayers,
            //     isPrivate,
            //     gameMode
            // );
            LobbyManager.Instance.CreateLobby(
                "TicTacToe Bet",
                2,
                // false,
                isPrivate,
                gameMode
            );
            Hide();
        });

        createPrivateRoomButton.onClick.AddListener(() => {
            LobbyManager.Instance.CreateLobby(
                "TicTacToe Bet",
                2,
                true,
                gameMode
            );
            Hide();
        });


        createPublicRoomButton.onClick.AddListener(() => {
            LobbyManager.Instance.CreateLobby(
                "TicTacToe Bet",
                2,
                false,
                gameMode
            );
            Hide();
        });

        joinLobbyButton.onClick.AddListener(() => {
            LobbyManager.Instance.JoinLobbyByCode(joinLobbyCode.text);
            Hide();
        });

        lobbyNameButton.onClick.AddListener(() => {
            UI_InputWindow.Show_Static("Lobby Name", lobbyName, "abcdefghijklmnopqrstuvxywzABCDEFGHIJKLMNOPQRSTUVXYWZ .,-1234567890", 20,
            () => {
                // Cancel
            },
            (string lobbyName) => {
                this.lobbyName = lobbyName;
                UpdateText();
            });
        });

        publicPrivateButton.onClick.AddListener(() => {
            isPrivate = !isPrivate;
            UpdateText();
        });

        maxPlayersButton.onClick.AddListener(() => {
            UI_InputWindow.Show_Static("Max Players", maxPlayers,
            () => {
                // Cancel
            },
            (int maxPlayers) => {
                this.maxPlayers = maxPlayers;
                UpdateText();
            });
        });

        gameModeButton.onClick.AddListener(() => {
            switch (gameMode) {
                default:
                case LobbyManager.GameMode.TicTacToe:
                    gameMode = LobbyManager.GameMode.RollDice;
                    break;
                case LobbyManager.GameMode.RollDice:
                    gameMode = LobbyManager.GameMode.TicTacToe;
                    break;
            }
            UpdateText();
        });

        Hide();
    }

    private void UpdateText() {
        lobbyNameText.text = lobbyName;
        publicPrivateText.text = isPrivate ? "Private" : "Public";
        maxPlayersText.text = maxPlayers.ToString();
        gameModeText.text = gameMode.ToString();
    }

    private void Hide() {
        gameObject.SetActive(false);
    }

    public void Show() {
        gameObject.SetActive(true);

        lobbyName = "MyLobby";
        isPrivate = false;
        maxPlayers = 2;
        gameMode = LobbyManager.GameMode.TicTacToe;

        UpdateText();
    }

}