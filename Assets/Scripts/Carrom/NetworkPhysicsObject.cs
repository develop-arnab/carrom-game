using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Authority-based physics control for the async telemetry replay architecture.
///
/// Also acts as the single source of truth for piece speed, abstracting over
/// the dual-reality physics system:
///   Authority  → CurrentSpeed = rb.linearVelocity.magnitude  (live Box2D data)
///   Spectator  → CurrentSpeed = position delta / fixedDeltaTime (kinematic MovePosition)
///
/// ContinuousSlidingSound reads CurrentSpeed so audio works identically on both machines.
/// </summary>
public class NetworkPhysicsObject : NetworkBehaviour
{
    private Rigidbody2D rb;
    private bool        hasAuthority;

    // Speed abstraction — valid for both Dynamic (authority) and Kinematic (spectator) bodies
    public float CurrentSpeed { get; private set; }
    private Vector2 previousPosition;

    [Header("Soft Braking")]
    [Tooltip("Speed below which the exponential brake engages (world units/s).")]
    [SerializeField] private float brakingThreshold  = 0.5f;
    [Tooltip("Velocity multiplier applied per FixedUpdate tick while braking. Lower = faster stop.")]
    [SerializeField] private float brakingMultiplier = 0.85f;

    // -------------------------------------------------------------------------
    // LIFECYCLE
    // -------------------------------------------------------------------------

    private void Awake()
    {
        rb               = GetComponent<Rigidbody2D>();
        previousPosition = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        hasAuthority = IsOwner;
        previousPosition = transform.position;
        SetPhysicsSimulation(hasAuthority);
        Debug.Log($"[NetworkPhysicsObject] {gameObject.name} — Authority:{hasAuthority} Simulated:{rb.simulated}");
    }

    // -------------------------------------------------------------------------
    // UNIFIED SPEED CALCULATION
    // -------------------------------------------------------------------------

    private void FixedUpdate()
    {
        // Always calculate kinematic delta — covers UI slider teleportation on authority
        // and MovePosition replay on spectator
        float kinematicSpeed = Vector2.Distance(transform.position, previousPosition) / Time.fixedDeltaTime;

        if (hasAuthority)
        {
            // Take the max: Box2D velocity wins during a real shot,
            // kinematic delta wins when the slider moves the piece directly
            CurrentSpeed = Mathf.Max(rb.linearVelocity.magnitude, kinematicSpeed);

            // Exponential soft brake — eliminates Box2D's long float-to-zero tail.
            // Only engages in the slow-roll window; full-speed shots are unaffected.
            float vel = rb.linearVelocity.magnitude;
            if (vel > 0.01f && vel < brakingThreshold)
            {
                rb.linearVelocity  *= brakingMultiplier;
                rb.angularVelocity *= brakingMultiplier;
            }
        }
        else
        {
            // Spectator: body is Kinematic, linearVelocity is always zero
            CurrentSpeed = kinematicSpeed;
        }

        previousPosition = transform.position;
    }

    // -------------------------------------------------------------------------
    // AUTHORITY CONTROL
    // -------------------------------------------------------------------------

    /// <summary>
    /// Switch between active (simulated) and spectating (not simulated) modes.
    /// Called during authority transfer between turns.
    /// </summary>
    public void SetAuthority(bool isActive)
    {
        hasAuthority     = isActive;
        previousPosition = transform.position; // reset delta so speed doesn't spike on switch
        SetPhysicsSimulation(isActive);
        Debug.Log($"[NetworkPhysicsObject] {gameObject.name} authority → {isActive}");
    }

    public void SetPhysicsSimulation(bool simulate)
    {
        if (rb != null) rb.simulated = simulate;
    }

    /// <summary>
    /// Update visual Transform without affecting Rigidbody2D physics state.
    /// Used by PlaybackEngine on the spectator to render interpolated positions.
    /// </summary>
    public void SetVisualTransform(Vector2 position, float rotation)
    {
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        transform.rotation = Quaternion.Euler(0, 0, rotation);
    }
}
