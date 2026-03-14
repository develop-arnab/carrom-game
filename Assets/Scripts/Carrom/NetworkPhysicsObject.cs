using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Authority-based physics control for async telemetry replay architecture.
/// Active player (Host OR Client) runs full local physics simulation with 0ms input lag.
/// Spectating player (Host OR Client) disables physics and uses telemetry playback.
/// </summary>
public class NetworkPhysicsObject : NetworkBehaviour
{
    private Rigidbody2D rb;
    private bool hasAuthority;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Authority-based physics control (NOT server-authoritative)
        // The player who owns this NetworkObject runs local physics
        // Active_Player: Rigidbody2D.simulated = true (full local physics, 0ms lag)
        // Spectating_Player: Rigidbody2D.simulated = false (no physics, telemetry playback)
        hasAuthority = IsOwner;
        SetPhysicsSimulation(hasAuthority);
        
        Debug.Log($"[Network] {gameObject.name} physics simulation: {rb.simulated} (Authority: {hasAuthority}, IsOwner: {IsOwner})");
    }
    
    /// <summary>
    /// Switch between active (simulated) and spectating (not simulated) modes.
    /// Called during authority transfer between turns.
    /// CRITICAL: This enables 0ms input lag for whichever player has the turn.
    /// </summary>
    /// <param name="isActive">True if this player is the Active_Player, false if Spectating_Player</param>
    public void SetAuthority(bool isActive)
    {
        hasAuthority = isActive;
        SetPhysicsSimulation(isActive);
        Debug.Log($"[Network] {gameObject.name} authority changed: {isActive}, physics simulation: {rb.simulated}");
    }
    
    /// <summary>
    /// Control Rigidbody2D.simulated property to enable/disable physics calculations.
    /// When simulated = true: Full Box2D physics runs locally (Active Player)
    /// When simulated = false: Zero physics overhead, PlaybackEngine controls transforms (Spectating Player)
    /// </summary>
    /// <param name="simulate">True to enable physics, false to disable</param>
    public void SetPhysicsSimulation(bool simulate)
    {
        if (rb != null)
        {
            rb.simulated = simulate;
        }
    }
    
    /// <summary>
    /// Update visual Transform without affecting Rigidbody2D physics state.
    /// Used by PlaybackEngine on Spectating_Player to render interpolated positions.
    /// </summary>
    /// <param name="position">Target position</param>
    /// <param name="rotation">Target rotation (Z-axis degrees)</param>
    public void SetVisualTransform(Vector2 position, float rotation)
    {
        // Directly modify Transform, not Rigidbody2D
        // This decouples visual rendering from physics state
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        transform.rotation = Quaternion.Euler(0, 0, rotation);
    }
}
