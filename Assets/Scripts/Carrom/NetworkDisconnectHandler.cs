using System.Collections;
using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.SceneManagement;

public class NetworkDisconnectHandler : MonoBehaviour
{
    [SerializeField] private GameObject disconnectMessagePanel;
    [SerializeField] private TMP_Text disconnectMessageText;
    
    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
        
        if (disconnectMessagePanel != null)
        {
            disconnectMessagePanel.SetActive(false);
        }
    }
    
    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[Network] Client {clientId} disconnected");
        
        // If opponent disconnected (not us)
        if (clientId != NetworkManager.Singleton.LocalClientId)
        {
            ShowDisconnectMessage("Opponent disconnected. Returning to main menu...");
            StartCoroutine(ReturnToMainMenuAfterDelay(2f));
        }
        // If we disconnected
        else
        {
            ShowDisconnectMessage("Disconnected from host. Returning to main menu...");
            StartCoroutine(ReturnToMainMenuAfterDelay(2f));
        }
    }
    
    private void ShowDisconnectMessage(string message)
    {
        if (disconnectMessagePanel != null)
        {
            disconnectMessagePanel.SetActive(true);
        }
        
        if (disconnectMessageText != null)
        {
            disconnectMessageText.text = message;
        }
        
        Debug.Log($"[Network] {message}");
    }
    
    private IEnumerator ReturnToMainMenuAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Clean up network objects
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsHost)
            {
                NetworkManager.Singleton.Shutdown();
            }
            else if (NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
        
        // Return to main menu
        SceneManager.LoadScene(0);
    }
}
