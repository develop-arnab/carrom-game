using UnityEngine;
using Unity.Netcode;

public class OpponentStrikerController : NetworkBehaviour
{
    [SerializeField] private Transform strikerForceField;
    
    private Rigidbody2D rb;
    
    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        
        // Set Client Rigidbody2D to kinematic (Host controls physics)
        if (IsSpawned && !IsServer)
        {
            rb.isKinematic = true;
        }
        
        // Hide force field initially
        if (strikerForceField != null)
        {
            strikerForceField.gameObject.SetActive(false);
        }
    }
    
    private void OnEnable()
    {
        // Determine Y position based on ownership in multiplayer
        float yPosition = 3.45f; // Default to top (opponent position)
        
        if (IsSpawned)
        {
            // In multiplayer, position based on whether this is the local player's striker
            // Local player's striker always at bottom, opponent's at top
            yPosition = IsOwner ? -4.57f : 3.45f;
        }
        
        // Reset position when enabled
        transform.position = new Vector3(0, yPosition, 0f);
        CollisionSoundManager.shouldBeStatic = true;
    }
    
    private void OnCollisionEnter2D(Collision2D other)
    {
        // Play the collision sound if the striker collides with the board
        if (other.gameObject.CompareTag("Board"))
        {
            GetComponent<AudioSource>().Play();
        }
    }
}
