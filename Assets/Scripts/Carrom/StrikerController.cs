using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class StrikerController : NetworkBehaviour
{
    [SerializeField]
    float strikerSpeed = 100f;

    [SerializeField]
    float maxScale = 1f;

    [SerializeField]
    Transform strikerForceField;

    [SerializeField]
    Slider strikerSlider;

    bool isMoving;
    bool isCharging;
    float maxForceMagnitude = 30f;
    Rigidbody2D rb;

    public static bool playerTurn;

    private void Start()
    {
        playerTurn = true;
        isMoving = false;
        rb = GetComponent<Rigidbody2D>();
        
        // Server-authoritative physics
        if (IsSpawned && !IsServer)
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
        else if (IsSpawned && IsServer)
        {
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
    }

    private void OnEnable()
    {
        // Determine Y position based on ownership in multiplayer
        float yPosition = -4.57f; // Default to bottom (local player position)
        
        if (IsSpawned)
        {
            // In multiplayer, position based on whether this is the local player's striker
            // Local player's striker always at bottom, opponent's at top
            yPosition = IsOwner ? -4.57f : 3.45f;
        }
        
        // Reset the position of the striker when it is enabled
        if (strikerSlider != null && (IsOwner || !IsSpawned))
        {
            transform.position = new Vector3(strikerSlider.value, yPosition, 0);
        }
        else
        {
            transform.position = new Vector3(0, yPosition, 0);
        }
        
        if (strikerForceField != null)
        {
            strikerForceField.LookAt(transform.position);
            strikerForceField.localScale = new Vector3(0, 0, 0);
        }
        
        CollisionSoundManager.shouldBeStatic = true;
    }

    private void Update()
    {
        // Check if the striker has come to a near stop and is not moving
        if (rb.linearVelocity.magnitude < 0.1f && !isMoving)
        {
            isMoving = true;
            StartCoroutine(OnMouseUp());
        }
    }

    private void OnMouseDown()
    {
        // In multiplayer, check if this is the correct striker for the current player
        if (IsSpawned)
        {
            CarromGameManager gm = FindObjectOfType<CarromGameManager>();
            if (gm != null)
            {
                // Determine which striker should be active based on turn and player role
                bool isHostTurn = gm.networkPlayerTurn.Value;
                bool isPlayerStriker = gameObject == gm.GetPlayerStriker();
                bool isEnemyStriker = gameObject == gm.GetEnemyStriker();
                
                // Host should only control playerStriker on Host's turn
                // Client should only control enemyStriker on Client's turn
                if (IsHost)
                {
                    if (!isPlayerStriker || !isHostTurn)
                    {
                        Debug.Log($"[Network] Host ignoring input - isPlayerStriker: {isPlayerStriker}, isHostTurn: {isHostTurn}");
                        return;
                    }
                }
                else // IsClient
                {
                    if (!isEnemyStriker || isHostTurn)
                    {
                        Debug.Log($"[Network] Client ignoring input - isEnemyStriker: {isEnemyStriker}, isHostTurn: {isHostTurn}");
                        return;
                    }
                }
            }
        }
        
        // If the striker is moving, disable charging and return
        if (rb.linearVelocity.magnitude > 0.1f)
        {
            isCharging = false;
            return;
        }

        // Determine correct Y position based on which striker this is
        float correctYPosition = -4.57f; // Default to bottom
        if (IsSpawned)
        {
            CarromGameManager gm = FindObjectOfType<CarromGameManager>();
            if (gm != null)
            {
                // Host's playerStriker at bottom, enemyStriker at top
                // Client's enemyStriker at bottom, playerStriker at top
                bool isPlayerStriker = gameObject == gm.GetPlayerStriker();
                correctYPosition = (IsHost && isPlayerStriker) || (!IsHost && !isPlayerStriker) ? -4.57f : 3.45f;
            }
        }
        
        // Reset the position of the striker if it is not at the correct y-axis position
        if (transform.position.y != correctYPosition)
        {
            transform.position = new Vector3(0, correctYPosition, 0);
        }

        // Enable charging and show the striker force field
        isCharging = true;
        strikerForceField.gameObject.SetActive(true);
        Debug.Log("[Network] Striker charging started");
    }

    private IEnumerator OnMouseUp()
    {
        isMoving = true;
        yield return new WaitForSeconds(0.1f);

        // If charging is not enabled, exit the coroutine
        if (!isCharging)
        {
            yield break;
        }

        strikerForceField.gameObject.SetActive(false);
        isCharging = false;
        yield return new WaitForSeconds(0.1f);

        // Calculate the direction and magnitude of the force based on the mouse position
        Vector3 direction = transform.position - Camera.main.ScreenToWorldPoint(Input.mousePosition);
        direction.z = 0f;
        float forceMagnitude = direction.magnitude * strikerSpeed;
        forceMagnitude = Mathf.Clamp(forceMagnitude, 0f, maxForceMagnitude);

        // In multiplayer, server applies physics
        if (IsSpawned)
        {
            if (IsServer)
            {
                // Host applies force directly
                rb.AddForce(direction.normalized * forceMagnitude, ForceMode2D.Impulse);
                CollisionSoundManager.shouldBeStatic = false;
            }
            else
            {
                // Client sends request to server
                RequestStrikerShootServerRpc(direction, forceMagnitude);
            }
        }
        else
        {
            // Single-player mode
            rb.AddForce(direction.normalized * forceMagnitude, ForceMode2D.Impulse);
            CollisionSoundManager.shouldBeStatic = false;
        }

        yield return new WaitForSeconds(0.1f);

        // Wait until the striker comes to a near stop
        yield return new WaitUntil(() => rb.linearVelocity.magnitude < 0.1f);

        // Wait for all objects to stop moving before switching turn
        CarromGameManager gm = FindObjectOfType<CarromGameManager>();
        if (gm != null)
        {
            yield return new WaitUntil(() => gm.AreAllObjectsStopped());
        }

        isMoving = false;
        
        // Switch turn
        if (IsSpawned)
        {
            if (IsServer && gm != null)
            {
                gm.SwitchTurn();
            }
        }
        else
        {
            playerTurn = false;
        }
        
        gameObject.SetActive(false);
    }

    private void OnMouseDrag()
    {
        // If charging is not enabled, return
        if (!isCharging)
        {
            return;
        }

        // Update the direction and scale of the striker force field based on the mouse position
        Vector3 direction = transform.position - Camera.main.ScreenToWorldPoint(Input.mousePosition);
        direction.z = 0f;
        strikerForceField.LookAt(transform.position + direction);

        float scaleValue = Vector3.Distance(transform.position, transform.position + direction / 4f);

        if (scaleValue > maxScale)
        {
            scaleValue = maxScale;
        }

        strikerForceField.localScale = new Vector3(scaleValue, scaleValue, scaleValue);
    }

    public void SetSliderX()
    {
        // Set the X position of the striker based on the slider value
        if (strikerSlider == null)
        {
            Debug.LogWarning("[Network] strikerSlider is null, cannot set position");
            return;
        }
        
        if (rb.linearVelocity.magnitude < 0.1f)
        {
            // Determine correct Y position based on ownership
            float yPosition = (IsSpawned && !IsOwner) ? 3.45f : -4.57f;
            
            float newX = strikerSlider.value;
            transform.position = new Vector3(newX, yPosition, 0);
            
            // In multiplayer, sync position to server
            if (IsSpawned && !IsServer)
            {
                RequestStrikerPositionServerRpc(newX);
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        // Play the collision sound if the striker collides with the board
        if (other.gameObject.CompareTag("Board"))
        {
            // GetComponent<AudioSource>().Play();
        }
    }
    [ServerRpc(RequireOwnership = false)]
    private void RequestStrikerPositionServerRpc(float xPosition, ServerRpcParams rpcParams = default)
    {
        // Validate position is within bounds
        if (xPosition < -3f || xPosition > 3f)
        {
            Debug.LogWarning($"[Network] Invalid striker position: {xPosition}");
            return;
        }

        // Determine correct Y position based on ownership
        float yPosition = IsOwner ? -4.57f : 3.45f;

        // Update striker position
        transform.position = new Vector3(xPosition, yPosition, 0);
        Debug.Log($"[Network] Striker position updated to: {xPosition}");
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStrikerShootServerRpc(Vector3 direction, float forceMagnitude, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        // Validate it's the player's turn
        CarromGameManager gm = FindObjectOfType<CarromGameManager>();
        if (gm != null)
        {
            bool isHostTurn = gm.networkPlayerTurn.Value;
            bool senderIsHost = senderId == NetworkManager.Singleton.LocalClientId;

            if (isHostTurn != senderIsHost)
            {
                Debug.LogWarning($"[Network] Invalid shot request from {senderId} - not their turn");
                return;
            }
        }

        // Validate force magnitude
        if (forceMagnitude < 0 || forceMagnitude > maxForceMagnitude)
        {
            Debug.LogWarning($"[Network] Invalid force magnitude: {forceMagnitude}");
            return;
        }

        // Apply force to striker on server
        rb.AddForce(direction.normalized * forceMagnitude, ForceMode2D.Impulse);
        CollisionSoundManager.shouldBeStatic = false;
        
        // Start coroutine on server to wait for movement to stop and switch turn
        StartCoroutine(WaitForShotCompleteAndSwitchTurn());
    }
    
    private IEnumerator WaitForShotCompleteAndSwitchTurn()
    {
        // Wait until the striker comes to a near stop
        yield return new WaitUntil(() => rb.linearVelocity.magnitude < 0.1f);

        // Wait for all objects to stop moving before switching turn
        CarromGameManager gm = FindObjectOfType<CarromGameManager>();
        if (gm != null)
        {
            yield return new WaitUntil(() => gm.AreAllObjectsStopped());
            
            // Switch turn on server
            gm.SwitchTurn();
            Debug.Log("[Network] Turn switched after client shot");
        }
        
        // Deactivate striker
        gameObject.SetActive(false);
    }

}
