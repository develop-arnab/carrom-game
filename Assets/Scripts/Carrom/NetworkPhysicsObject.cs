using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-authoritative physics with smooth client-side interpolation buffer
/// </summary>
public class NetworkPhysicsObject : NetworkBehaviour
{
    private Rigidbody2D rb;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Server-authoritative physics
        if (!IsServer)
        {
            // Client: kinematic with interpolation
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            Debug.Log($"[Network] {gameObject.name} set to kinematic on Client");
        }
        else
        {
            // Server: full physics simulation
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            Debug.Log($"[Network] {gameObject.name} physics active on Host");
        }
    }
}
