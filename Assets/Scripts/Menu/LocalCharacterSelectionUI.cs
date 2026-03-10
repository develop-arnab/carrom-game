using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode; // only for starting host/client via NetworkManager

// This script is a local (non-networked) version of the Character Selection UI.
// Attach it to a root UI GameObject that contains the same UI elements used in your CharacterSelection scene.
public class LocalCharacterSelectionUI : MonoBehaviour
{
    [Header("Data")]
    public CharacterDataSO[] charactersData; // assign same assets as in CharacterSelectionManager

    [Header("UI References")]
    public Image characterImage;
    public TextMeshProUGUI characterNameText;
    public GameObject border; // optional, same as container in CharacterSelection
    public GameObject borderReady; // optional
    public Button leftButton;
    public Button rightButton;
    public Button startHostButton;
    public Button startClientButton;
    public Button authButton;
    public Button readyButton; // optional if you want a ready toggle locally
    public AudioClip changeClip;
    public AudioClip confirmClip;
    [SerializeField] private Button joinLobbyButton;
    [SerializeField] private TMP_InputField joinLobbyCode;

    private int m_selectedIndex = 0;
    private SceneName nextScene = SceneName.CharacterSelection;
    private AudioSource m_audio;
    private LobbyManager.GameMode gameMode;
    private void Awake()
    {
        m_audio = GetComponent<AudioSource>();
        if (m_audio == null)
            m_audio = gameObject.AddComponent<AudioSource>();
    }

    private void Start()
    {
        // If there's previously stored selection, use it
/*        if (NonNetworkSelectionData.SelectedCharacter >= 0 && NonNetworkSelectionData.SelectedCharacter < charactersData.Length)
        {
            m_selectedIndex = NonNetworkSelectionData.SelectedCharacter;
        }
        else
        {
            m_selectedIndex = 0;
            NonNetworkSelectionData.SelectedCharacter = m_selectedIndex;
        }*/

        RefreshUI();

        // Hook buttons
        if (leftButton != null) leftButton.onClick.AddListener(() => ChangeSelection(-1));
        if (rightButton != null) rightButton.onClick.AddListener(() => ChangeSelection(1));
        if (startHostButton != null) startHostButton.onClick.AddListener(StartHostClicked);
        if (startClientButton != null) startClientButton.onClick.AddListener(StartClientClicked);
        if (readyButton != null) readyButton.onClick.AddListener(OnReadyClicked);
        if (joinLobbyButton != null)  {  
        joinLobbyButton.onClick.AddListener(() => {
            LobbyManager.Instance.JoinLobbyByCode(joinLobbyCode.text);
        });
        }
/*        if (authButton != null)  {  
        authButton.onClick.AddListener(() => {*/
            Debug.Log("CLICKED AUTH");
            int randomNumber = UnityEngine.Random.Range(1000, 10000);
            string playerName = "Player" + randomNumber;
            LobbyManager.Instance.Authenticate(playerName);
     /*   });
        }*/
    }

    private void Update()
    {
        // keyboard support like CharacterSelection
        if (Input.GetKeyDown(KeyCode.A))
        {
            ChangeSelection(-1);
        }
        else if (Input.GetKeyDown(KeyCode.D))
        {
            ChangeSelection(1);
        }
    }

    private void ChangeSelection(int delta)
    {
        if (charactersData == null || charactersData.Length == 0) return;

        m_selectedIndex += delta;
        if (m_selectedIndex < 0) m_selectedIndex = charactersData.Length - 1;
        if (m_selectedIndex >= charactersData.Length) m_selectedIndex = 0;

        // store selection for later networked scene
        NonNetworkSelectionData.SelectedCharacter = m_selectedIndex;

        RefreshUI();

        if (changeClip != null && m_audio != null)
            m_audio.PlayOneShot(changeClip);
    }

    private void RefreshUI()
    {
        if (charactersData == null || charactersData.Length == 0) return;

        var data = charactersData[m_selectedIndex];
        if (characterImage != null) characterImage.sprite = data.characterSprite;
        if (characterNameText != null) characterNameText.text = data.characterName;

        // Extra UI states to mimic the original:
        if (border != null) border.SetActive(true);
        if (borderReady != null) borderReady.SetActive(false);
    }

    private void OnReadyClicked()
    {
        // local ready (optional). We can just play a clip.
        if (confirmClip != null && m_audio != null)
            m_audio.PlayOneShot(confirmClip);
    }

    private void StartHostClicked()
    {


        /*NetworkManager.Singleton.StartHost();*/
        LobbyManager.Instance.CreateLobby(
            "TicTacToe Bet",
            2,
            true,
            gameMode
        );
        // AudioManager.Instance.PlaySoundEffect(m_confirmClip);
        /*LoadingSceneManager.Instance.LoadScene(nextScene);*/
        // Persist selection into static holder (already stored by ChangeSelection)
        // if (NonNetworkSelectionData.SelectedCharacter < 0)
        //     NonNetworkSelectionData.SelectedCharacter = m_selectedIndex;

        // // Start host and let NetworkManager handle scene transitions
        // if (Unity.Netcode.NetworkManager.Singleton != null)
        // {
        //     NetworkManager.Singleton.StartHost();
        // }
        // else
        // {
        //     Debug.LogWarning("NetworkManager singleton is null. Make sure NetworkManager exists in the Bootstrap scene.");
        // }
    }

    private void StartClientClicked()
    {
        NetworkManager.Singleton.StartClient();
        // if (NonNetworkSelectionData.SelectedCharacter < 0)
        //     NonNetworkSelectionData.SelectedCharacter = m_selectedIndex;

        // if (Unity.Netcode.NetworkManager.Singleton != null)
        // {
        //     NetworkManager.Singleton.StartClient();
        // }
        // else
        // {
        //     Debug.LogWarning("NetworkManager singleton is null. Make sure NetworkManager exists in the Bootstrap scene.");
        // }
    }
}