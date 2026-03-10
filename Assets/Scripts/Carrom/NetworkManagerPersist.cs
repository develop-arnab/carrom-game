using UnityEngine;

/// <summary>
/// Ensures NetworkManager persists between scene loads
/// </summary>
public class NetworkManagerPersist : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
}
