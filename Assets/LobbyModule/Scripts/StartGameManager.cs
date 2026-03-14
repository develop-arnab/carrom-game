using Unity.Netcode.Transports.UTP;
using Unity.Netcode;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Relay;
using UnityEngine;

public class StartGameManager : MonoBehaviour {

[SerializeField]
    private SceneName nextScene = SceneName.CharacterSelection;

    private void Start() {
        LobbyManager.Instance.OnLobbyStartGame += LobbyManager_OnLobbyStartGame;
    }

    private void LobbyManager_OnLobbyStartGame(object sender, LobbyManager.LobbyEventArgs e) {
        // Start Game!
        if (LobbyManager.IsHost) {
            CreateRelay();
        } else {
            JoinRelay(LobbyManager.RelayJoinCode);
        }
    }

    public void StartHost() {
        NetworkManager.Singleton.StartHost();
        LoadingSceneManager.Instance.LoadScene(nextScene);
    }

    public void StartClient() {
        NetworkManager.Singleton.StartClient();
    }


    private async void CreateRelay() {
        try {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);

            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log("Allocated Relay JoinCode: " + joinCode);

            RelayServerData relayServerData = new RelayServerData(allocation, "wss");

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            StartHost();

            LobbyManager.Instance.SetRelayJoinCode(joinCode);
        } catch (RelayServiceException e) {
            Debug.Log(e);
        }
    }

    private async void JoinRelay(string joinCode) {
        try {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData relayServerData = new RelayServerData(joinAllocation, "wss");

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            StartClient();
        } catch (RelayServiceException e) {
            Debug.Log(e);
        }
    }

}