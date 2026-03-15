using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class StrikerController : NetworkBehaviour
{
    [SerializeField] float maxScale = 1f;
    [SerializeField] float maxDragDistance = 4f;
    [SerializeField] Transform strikerForceField;
    [SerializeField] Slider strikerSlider;

    bool isMoving;
    bool isCharging;
    float maxForceMagnitude = 30f;
    Rigidbody2D rb;
    AudioSource audioSource;

    // Manual speed tracking — needed because Kinematic bodies report zero relativeVelocity
    // on collision, so we derive speed from position delta in FixedUpdate instead.
    Vector3 previousPosition;
    float   currentSpeed;

    public static bool playerTurn;

    // -------------------------------------------------------------------------
    // LIFECYCLE
    // -------------------------------------------------------------------------

    private void Start()
    {
        playerTurn   = true;
        rb           = GetComponent<Rigidbody2D>();
        audioSource  = GetComponent<AudioSource>();
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        previousPosition = transform.position;
    }

    /// <summary>
    /// OnEnable delegates entirely to ResetToBaseline so there is one
    /// canonical reset path used by both initial spawn and turn handoff.
    /// </summary>
    private void OnEnable()
    {
        // rb may not be assigned yet if OnEnable fires before Start (first activation)
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        ResetToBaseline();
    }

    /// <summary>
    /// NGO ownership callback — fires on the NEW owner the moment authority transfers.
    /// This is the canonical trigger for a turn reset in the single-striker architecture.
    /// </summary>
    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        Debug.Log("[StrikerController] Ownership gained — resetting to baseline");
        ResetToBaseline();
    }

    // -------------------------------------------------------------------------
    // RESET — single source of truth for striker ready-state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Wipes physics state, snaps the striker to the new owner's Y-baseline,
    /// and broadcasts the position so the spectator sees it immediately.
    /// </summary>
    private void ResetToBaseline()
    {
        // Wipe physics residuals
        isMoving   = false;
        isCharging = false;
        if (rb != null)
        {
            rb.linearVelocity  = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // Hide force field
        if (strikerForceField != null)
            strikerForceField.localScale = Vector3.zero;

        if (!IsSpawned)
        {
            // Single-player: always bottom
            float x = strikerSlider != null ? strikerSlider.value : 0f;
            SetPosition(x, -4.57f);
            return;
        }

        if (!IsOwner) return; // Non-owners wait for SyncAimClientRpc to move them

        // Host sits at the bottom, Client at the top
        float y = IsServer ? -4.57f : 3.45f;
        float sliderX = strikerSlider != null ? strikerSlider.value : 0f;
        SetPosition(sliderX, y);

        // Broadcast starting position so spectator sees the striker appear at the baseline
        if (IsServer) SyncAimClientRpc(sliderX, y);
        else          RequestAimServerRpc(sliderX, y);
    }

    /// <summary>Moves the striker and keeps the force-field orientation consistent.</summary>
    private void SetPosition(float x, float y)
    {
        transform.position = new Vector3(x, y, 0);
        if (strikerForceField != null)
            strikerForceField.LookAt(transform.position);
    }

    // -------------------------------------------------------------------------
    // INPUT
    // -------------------------------------------------------------------------

    private void FixedUpdate()
    {
        // Track speed manually — Kinematic bodies report zero relativeVelocity on collision
        currentSpeed     = Vector3.Distance(transform.position, previousPosition) / Time.fixedDeltaTime;
        previousPosition = transform.position;
    }

    private void Update()
    {
        if (!IsOwner && IsSpawned) return;

        if (rb.linearVelocity.magnitude < 0.1f && !isMoving)
        {
            isMoving = true;
            StartCoroutine(OnMouseUp());
        }
    }

    private void OnMouseDown()
    {
        if (!IsOwner && IsSpawned) return;

        if (rb.linearVelocity.magnitude > 0.1f)
        {
            isCharging = false;
            return;
        }

        // Snap to correct Y if drifted
        float correctY = (!IsSpawned || IsServer) ? -4.57f : 3.45f;
        if (Mathf.Abs(transform.position.y - correctY) > 0.01f)
            transform.position = new Vector3(transform.position.x, correctY, 0);

        isCharging = true;
        strikerForceField.gameObject.SetActive(true);

        // Sync: tell spectator to activate the force field at the striker's current position
        if (IsSpawned)
            SyncForceFieldServerRpc(true, transform.position, Vector3.zero);
    }

    private IEnumerator OnMouseUp()
    {
        isMoving = true;
        yield return new WaitForSeconds(0.1f);

        if (!isCharging) yield break;

        strikerForceField.gameObject.SetActive(false);
        isCharging = false;

        // Sync: tell spectator to deactivate the force field before the shot fires
        if (IsSpawned)
            SyncForceFieldServerRpc(false, Vector3.zero, Vector3.zero);
        yield return new WaitForSeconds(0.1f);

        Vector3 direction = transform.position - Camera.main.ScreenToWorldPoint(Input.mousePosition);
        direction.z = 0f;
        float dragPercentage = Mathf.Clamp01(direction.magnitude / maxDragDistance);
        float forceMagnitude = dragPercentage * maxForceMagnitude;

        if (IsSpawned)
        {
            CarromGameManager gm = FindObjectOfType<CarromGameManager>();
            // Active player calls OnShotStart locally — recording runs on the owner's machine
            if (gm != null) gm.OnShotStart();

            rb.AddForce(direction.normalized * forceMagnitude, ForceMode2D.Impulse);
        }
        else
        {
            rb.AddForce(direction.normalized * forceMagnitude, ForceMode2D.Impulse);
        }

        yield return new WaitForSeconds(0.1f);
        yield return new WaitUntil(() => rb.linearVelocity.magnitude < 0.1f);

        CarromGameManager gm2 = FindObjectOfType<CarromGameManager>();
        if (gm2 != null)
            yield return new WaitUntil(() => gm2.AreAllObjectsStopped());

        isMoving = false;

        if (IsSpawned)
        {
            // Active player calls OnShotComplete locally — recording stops on the owner's machine
            if (gm2 != null) gm2.OnShotComplete();
        }
        else
        {
            playerTurn = false;
        }

        // Single-player turn flip — striker stays active in multiplayer
        if (!IsSpawned)
            gameObject.SetActive(false);
    }

    private void OnMouseDrag()
    {
        if (!IsOwner && IsSpawned) return;
        if (!isCharging) return;

        Vector3 direction = transform.position - Camera.main.ScreenToWorldPoint(Input.mousePosition);
        direction.z = 0f;
        strikerForceField.LookAt(transform.position + direction);

        float scaleValue = Mathf.Clamp01(direction.magnitude / maxDragDistance) * maxScale;
        strikerForceField.localScale = new Vector3(scaleValue, scaleValue, scaleValue);

        // Sync: broadcast look target and scale so spectator's arrow mirrors the drag
        if (IsSpawned)
        {
            Vector3 lookTarget = transform.position + direction;
            Vector3 scale      = new Vector3(scaleValue, scaleValue, scaleValue);
            SyncForceFieldServerRpc(true, lookTarget, scale);
        }
    }

    // -------------------------------------------------------------------------
    // SLIDER AIM
    // -------------------------------------------------------------------------

    public void SetSliderX()
    {
        if (!IsOwner && IsSpawned) return;
        if (strikerSlider == null) return;

        if (rb.linearVelocity.magnitude < 0.1f)
        {
            float y = (!IsSpawned || IsServer) ? -4.57f : 3.45f;
            float x = strikerSlider.value;
            transform.position = new Vector3(x, y, 0);

            if (IsSpawned)
            {
                if (IsServer) SyncAimClientRpc(x, y);
                else          RequestAimServerRpc(x, y);
            }
        }
    }

    // -------------------------------------------------------------------------
    // RPCs
    // -------------------------------------------------------------------------

    [ServerRpc(RequireOwnership = false)]
    private void RequestAimServerRpc(float x, float y, ServerRpcParams rpcParams = default)
    {
        if (x < -3f || x > 3f) return;
        transform.position = new Vector3(x, y, 0);
        SyncAimClientRpc(x, y);
    }

    [ClientRpc]
    private void SyncAimClientRpc(float x, float y)
    {
        if (IsOwner) return;
        transform.position = new Vector3(x, y, 0);
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyServerShotCompleteServerRpc(ServerRpcParams rpcParams = default)
    {
        CarromGameManager gm = FindObjectOfType<CarromGameManager>();
        if (gm != null) gm.OnShotComplete();
        // No SetActive(false) — single striker stays alive, OnGainedOwnership resets it
    }

    /// <summary>
    /// Relays force field visual state from the owner to the server,
    /// which then broadcasts it to all non-owners via ClientRpc.
    /// active=false deactivates the GameObject; active=true applies lookTarget + scale.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SyncForceFieldServerRpc(bool active, Vector3 lookTarget, Vector3 scale, ServerRpcParams rpcParams = default)
    {
        SyncForceFieldClientRpc(active, lookTarget, scale);
    }

    [ClientRpc]
    private void SyncForceFieldClientRpc(bool active, Vector3 lookTarget, Vector3 scale)
    {
        if (IsOwner) return; // Owner already rendered this locally with 0-lag

        if (!active)
        {
            strikerForceField.gameObject.SetActive(false);
            return;
        }

        strikerForceField.gameObject.SetActive(true);
        strikerForceField.LookAt(lookTarget);
        strikerForceField.localScale = scale;
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        if (currentSpeed <= 0.1f) return;
        if (!other.gameObject.CompareTag("Board") &&
            !other.gameObject.CompareTag("White") &&
            !other.gameObject.CompareTag("Black") &&
            !other.gameObject.CompareTag("Queen")) return;

        if (audioSource == null) return;
        float volume = Mathf.Clamp01(currentSpeed / 10f);
        audioSource.volume = volume;
        audioSource.Play();

        // Broadcast so TelemetryRecorder can log this into the audio track
        CollisionSoundManager.BroadcastCollisionSound(transform.position, volume);
    }
}
