using UnityEngine;
using Unity.Netcode;

public class MainMenuNetworkButtons : MonoBehaviour
{
public void StartHost()
{
if (NetworkManager.Singleton == null)
{
Debug.LogError("NetworkManager not found. Make sure Bootstrap scene has the NetworkManager and you started from Bootstrap.");
return;
}
NetworkManager.Singleton.StartHost();
}
public void StartClient()
{
    if (NetworkManager.Singleton == null)
    {
        Debug.LogError("NetworkManager not found. Make sure Bootstrap scene has the NetworkManager and you started from Bootstrap.");
        return;
    }
    NetworkManager.Singleton.StartClient();
}
}