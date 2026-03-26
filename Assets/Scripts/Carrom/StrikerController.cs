using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class StrikerController : NetworkBehaviour
{
    [SerializeField] float maxScale = 1f;
    [SerializeField] float maxDragDistance = 2f;
    [SerializeField] Transform strikerForceField;
    [SerializeField] Slider strikerSlider;

    // --- AAA Aiming Tuning ---
    [SerializeField] float visualLengthMultiplier = 2.5f;
    [SerializeField] float aimDeadzone            = 0.2f;
    [SerializeField] float aimSmoothSpeed         = 15f;

    bool isMoving;
    bool isCharging;
    bool isDragging;          // true while finger/mouse is locked onto the striker
    float maxForceMagnitude = 30f;
    Rigidbody2D rb;
    AudioSource audioSource;

    // Cached world-space input position — shared between grab, drag, and release
    Vector3 inputWorldPos;

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
    /// Position and state reset is handled exclusively by ResetToBaselineClientRpc and
    /// TriggerBotShot, so this callback is intentionally left as a no-op.
    /// </summary>
    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        Debug.Log("[StrikerController] Ownership gained");
    }

    /// <summary>
    /// NGO ownership callback — fires on the OLD owner when authority leaves.
    /// No position snapping here — ResetToBaselineClientRpc handles the incoming player's reset.
    /// </summary>
    public override void OnLostOwnership()
    {
        base.OnLostOwnership();
        Debug.Log("[StrikerController] Ownership lost");
    }

    // -------------------------------------------------------------------------
    // RESET — single source of truth for striker ready-state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired by the server when the active player earns an extra turn (turn retention).
    /// Resets the striker to baseline without an ownership transfer.
    /// </summary>
    [ClientRpc]
    public void RetainTurnResetClientRpc()
    {
        if (!IsOwner) return; // only the active player needs to reset
        Debug.Log("[StrikerController] RetainTurnResetClientRpc — resetting for extra turn");
        ResetToBaseline();
    }

    /// <summary>
    /// Fired by the server after advancing to a human seat.
    /// Only executes on the client whose LocalClientId matches the new active seat's OwnerClientId.
    /// </summary>
    [ClientRpc]
    public void ResetToBaselineClientRpc(int seatIndex)
    {
        CarromGameManager gm = CarromGameManager.Instance != null
            ? CarromGameManager.Instance
            : FindObjectOfType<CarromGameManager>();
        if (gm == null)
        {
            Debug.LogError("[SC:TELEM] ResetToBaselineClientRpc: CarromGameManager not found!");
            return;
        }

        ulong activeOwner = gm.GetSeatOwnerClientId(seatIndex);
        ulong localId     = NetworkManager.Singleton.LocalClientId;
        Debug.Log($"[SC:TELEM] ResetToBaselineClientRpc — seatIndex={seatIndex}, activeOwner={activeOwner}, localId={localId}, match={localId == activeOwner}");

        if (localId != activeOwner)
        {
            Debug.Log($"[SC:TELEM] Ownership check FAILED — skipping reset (not our turn)");
            return;
        }

        Debug.Log($"[SC:TELEM] Ownership check PASSED — calling ResetToBaseline({seatIndex})");
        ResetToBaseline(seatIndex);
    }

    // East/West rail X positions for seats 1 and 3
    [SerializeField] float eastRailX =  4.57f;
    [SerializeField] float westRailX = -4.57f;

    /// <summary>
    /// Seat-aware baseline reset. Seat 0 (South) and 2 (North) use Y-axis positioning;
    /// Seat 1 (East) and 3 (West) use X-axis positioning.
    /// </summary>
    public void ResetToBaseline(int seatIndex)
    {
        // Wipe physics residuals
        isMoving   = false;
        isCharging = false;
        isDragging = false;
        if (rb != null)
        {
            rb.linearVelocity  = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        if (strikerForceField != null)
            strikerForceField.localScale = Vector3.zero;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = true;

        float sliderVal = strikerSlider != null ? strikerSlider.value : 0f;

        switch (seatIndex)
        {
            case 0: SetPosition(sliderVal,  -4.57f); break;  // South — X-axis movement
            case 1: SetPosition(eastRailX,  sliderVal); break; // East  — Y-axis movement
            case 2: SetPosition(sliderVal,   3.45f); break;  // North — X-axis movement
            case 3: SetPosition(westRailX,  sliderVal); break; // West  — Y-axis movement
            default: SetPosition(sliderVal, -4.57f); break;
        }

        // Broadcast starting position
        if (IsSpawned)
        {
            if (IsServer) SyncAimClientRpc(transform.position.x, transform.position.y);
            else          RequestAimServerRpc(transform.position.x, transform.position.y);
        }
    }

    /// <summary>Parameterless overload — resolves seat from CarromGameManager.activeSeatIndex.</summary>
    public void ResetToBaseline()
    {
        if (!IsSpawned)
        {
            // Single-player: always seat 0 (South)
            isMoving = isCharging = isDragging = false;
            if (rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }
            if (strikerForceField != null) strikerForceField.localScale = Vector3.zero;
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = true;
            float x = strikerSlider != null ? strikerSlider.value : 0f;
            SetPosition(x, -4.57f);
            return;
        }

        CarromGameManager gm = CarromGameManager.Instance != null
            ? CarromGameManager.Instance
            : FindObjectOfType<CarromGameManager>();

        int seatIdx = gm != null ? (int)gm.activeSeatIndex.Value : (IsServer ? 0 : 2);
        ResetToBaseline(seatIdx);
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

    // -------------------------------------------------------------------------
    // INPUT — unified raycast (Desktop + Mobile)
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (IsSpawned && NetworkManager.Singleton.LocalClientId != CarromGameManager.Instance.GetActiveSeatOwnerClientId()) return;

        // --- Determine raw screen position and phase from mouse OR touch ---
        bool pressed  = false;
        bool held     = false;
        bool released = false;
        Vector3 screenPos = Vector3.zero;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            screenPos = touch.position;
            pressed   = touch.phase == TouchPhase.Began;
            held      = touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary;
            released  = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
        }
        else
        {
            screenPos = Input.mousePosition;
            pressed   = Input.GetMouseButtonDown(0);
            held      = Input.GetMouseButton(0);
            released  = Input.GetMouseButtonUp(0);
        }

        Camera cam = Camera.main;
        Vector3 worldPos = cam.ScreenToWorldPoint(screenPos);
        worldPos.z = 0f;

        // --- GRAB ---
        if (pressed && !isDragging && !isMoving)
        {
            if (rb.linearVelocity.magnitude > 0.1f) return;

            RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero);
            if (hit.collider == null || hit.collider.gameObject != gameObject) return;

            float correctY = (!IsSpawned || IsServer) ? -4.57f : 3.45f;
            if (Mathf.Abs(transform.position.y - correctY) > 0.01f)
                transform.position = new Vector3(transform.position.x, correctY, 0);

            isDragging = true;
            isCharging = true;
            strikerForceField.gameObject.SetActive(true);

            if (IsSpawned)
                SyncForceFieldServerRpc(true, transform.position, Vector3.zero);
        }

        // --- DRAG ---
        if (held && isDragging && isCharging)
        {
            Vector3 direction = transform.position - worldPos;
            direction.z = 0f;

            if (direction.magnitude < aimDeadzone)
            {
                // Inside deadzone — hide arrow but keep grip
                strikerForceField.localScale = Vector3.zero;
            }
            else
            {
                // Smooth rotation via Slerp
                Vector3 smoothedForward = Vector3.Slerp(
                    strikerForceField.forward,
                    direction.normalized,
                    Time.deltaTime * aimSmoothSpeed
                );
                strikerForceField.LookAt(transform.position + smoothedForward);

                // Visual length decoupled from physics drag
                float dragPercentage = Mathf.Clamp01(direction.magnitude / maxDragDistance);
                float scaleValue     = dragPercentage * maxScale * visualLengthMultiplier;
                strikerForceField.localScale = new Vector3(scaleValue, scaleValue, scaleValue);

                if (IsSpawned)
                    SyncForceFieldServerRpc(true, transform.position + smoothedForward, new Vector3(scaleValue, scaleValue, scaleValue));
            }
        }

        // --- RELEASE ---
        if (released && isDragging)
        {
            Vector3 finalDirection = transform.position - worldPos;
            finalDirection.z = 0f;

            if (finalDirection.magnitude < aimDeadzone)
            {
                // Released inside deadzone — cancel the shot
                isDragging = false;
                isCharging = false;
                strikerForceField.gameObject.SetActive(false);
                if (IsSpawned)
                    SyncForceFieldServerRpc(false, Vector3.zero, Vector3.zero);
            }
            else
            {
                isDragging = false;
                if (!isMoving)
                {
                    isMoving = true;
                    StartCoroutine(FireShot(worldPos));
                }
            }
        }

        // --- AUTO-FIRE when velocity settles (single-player safety net) ---
        if (!IsSpawned && rb.linearVelocity.magnitude < 0.1f && !isMoving)
        {
            isMoving = true;
            StartCoroutine(FireShot(worldPos));
        }
    }

    /// <summary>
    /// Universal shot entry point — called by human input (via FireShot) and AI bots (via TriggerBotShot).
    /// Guard: only executes when IsOwner OR IsServer (for bot shots).
    /// </summary>
    public void ExecuteShot(Vector2 direction, float forceMagnitude)
    {
        if (!IsOwner && !IsServer) return;

        if (strikerForceField != null)
            strikerForceField.gameObject.SetActive(false);

        CarromGameManager gm = CarromGameManager.Instance != null
            ? CarromGameManager.Instance
            : FindObjectOfType<CarromGameManager>();
        if (gm != null) gm.OnShotStart();

        rb.AddForce(direction.normalized * forceMagnitude, ForceMode2D.Impulse);

        StartCoroutine(WaitForShotComplete());
    }

    /// <summary>
    /// Post-shot wait coroutine — waits for all objects to stop, then calls OnShotComplete.
    /// Extracted from FireShot so ExecuteShot can reuse it identically.
    /// </summary>
    private IEnumerator WaitForShotComplete()
    {
        yield return new WaitForSeconds(0.1f);
        yield return new WaitUntil(() => rb.linearVelocity.magnitude < 0.1f);

        CarromGameManager gm = CarromGameManager.Instance != null
            ? CarromGameManager.Instance
            : FindObjectOfType<CarromGameManager>();
        if (gm != null)
            yield return new WaitUntil(() => gm.AreAllObjectsStopped());

        isMoving = false;

        if (IsSpawned)
        {
            if (gm != null) gm.OnShotComplete();
        }
        else
        {
            playerTurn = false;
            gameObject.SetActive(false);
        }
    }

    private IEnumerator FireShot(Vector3 releaseWorldPos)
    {
        if (!isCharging) { isMoving = false; yield break; }

        isCharging = false;
        if (IsSpawned)
            SyncForceFieldServerRpc(false, Vector3.zero, Vector3.zero);
        yield return new WaitForSeconds(0.1f);

        Vector3 dir3 = transform.position - releaseWorldPos;
        dir3.z = 0f;
        float dragPercentage = Mathf.Clamp01(dir3.magnitude / maxDragDistance);
        float forceMagnitude = dragPercentage * maxForceMagnitude;

        ExecuteShot(new Vector2(dir3.x, dir3.y), forceMagnitude);
    }

    // -------------------------------------------------------------------------
    // SLIDER AIM
    // -------------------------------------------------------------------------

    public void SetSliderX()
    {
        if (IsSpawned && NetworkManager.Singleton.LocalClientId != CarromGameManager.Instance.GetActiveSeatOwnerClientId()) return;
        if (strikerSlider == null) return;
        if (rb.linearVelocity.magnitude >= 0.1f) return;

        CarromGameManager gm = CarromGameManager.Instance != null
            ? CarromGameManager.Instance
            : FindObjectOfType<CarromGameManager>();
        int seatIdx = gm != null ? gm.activeSeatIndex.Value : (IsServer ? 0 : 2);

        float val = strikerSlider.value;
        float x = transform.position.x;
        float y = transform.position.y;

        // Seats 0/2 move along X; seats 1/3 move along Y
        if (seatIdx == 1 || seatIdx == 3)
            y = val;
        else
            x = val;

        transform.position = new Vector3(x, y, 0);

        if (IsSpawned)
        {
            if (IsServer) SyncAimClientRpc(x, y);
            else          RequestAimServerRpc(x, y);
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

    /// <summary>
    /// Server-only: broadcasts the striker's current position to all clients.
    /// Called by CarromAIBrain at the start and end of the striker slide animation.
    /// </summary>
    public void BroadcastPositionToClients()
    {
        if (!IsServer) return;
        SyncAimClientRpc(transform.position.x, transform.position.y);
    }

    /// <summary>
    /// Server-only: drives the force-field aiming arrow for bot shots.
    /// Mirrors the visual state the human drag code produces so remote clients
    /// see the bot "charging up" before it fires.
    /// lookTarget — world-space point the arrow should point toward
    /// scale       — uniform scale applied to strikerForceField (ramp from 0 → max)
    /// </summary>
    public void SimulateAimingVisuals(Vector3 lookTarget, Vector3 scale)
    {
        if (!IsServer) return;
        if (strikerForceField == null) return;

        strikerForceField.gameObject.SetActive(true);
        strikerForceField.LookAt(lookTarget);
        strikerForceField.localScale = scale;

        // Broadcast to all clients so they see the animated arrow
        SyncForceFieldClientRpc(true, lookTarget, scale);
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
