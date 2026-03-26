using System.Collections;
using UnityEngine;
using TMPro;
using Unity.Netcode;

public struct BaselineData
{
    public bool  isHorizontal; // true = seats 0/2 (Y fixed), false = seats 1/3 (X fixed)
    public float fixedAxis;    // the fixed coordinate value
    public float rangeMin;     // slider min
    public float rangeMax;     // slider max
}

/// <summary>
/// Handles pocket triggers using the Graveyard pattern.
/// Scoring is fully delegated to CarromGameManager via the Shot Ledger.
/// BoardScript only handles: physics (SendToGraveyard) + ledger reporting.
/// </summary>
public class BoardScript : NetworkBehaviour
{
    // Off-screen graveyard base — pocket coords are encoded as (1000 + x, 1000 + y)
    // Tweak in the Inspector to nudge the ghost spawn point onto the visual pocket center.
    [SerializeField] private Vector2 ghostSpawnOffset = Vector2.zero;

    private TextMeshProUGUI popUpText;

    private void Start()
    {
        popUpText = GameObject.Find("UpdatesText").GetComponent<TextMeshProUGUI>();
    }

    // -------------------------------------------------------------------------
    // TRIGGER
    // -------------------------------------------------------------------------

    private void OnTriggerEnter2D(Collider2D other)
    {
        // In multiplayer, only the piece owner (active player) processes pocketing.
        // The spectator ignores it — their replay will show the piece sliding in.
        if (IsSpawned)
        {
            NetworkObject netObj = other.gameObject.GetComponent<NetworkObject>();
            if (netObj != null && !netObj.IsOwner) return;
        }

        GetComponent<AudioSource>().Play();

        switch (other.gameObject.tag)
        {
            case "Striker":
                HandleStriker(other);
                break;
            case "Black":
                HandleBlack(other, transform.position);
                break;
            case "White":
                HandleWhite(other, transform.position);
                break;
            case "Queen":
                HandleQueen(other, transform.position);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // POCKET HANDLERS
    // -------------------------------------------------------------------------

    private void HandleStriker(Collider2D other)
    {
        other.gameObject.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        if (IsSpawned) ReportPocketedCoinServerRpc(CoinType.Striker);
    }

    private void HandleBlack(Collider2D other, Vector3 pocketCenter)
    {
        SendToGraveyard(other, pocketCenter);
        if (IsSpawned) ReportPocketedCoinServerRpc(CoinType.Black);
    }

    private void HandleWhite(Collider2D other, Vector3 pocketCenter)
    {
        SendToGraveyard(other, pocketCenter);
        if (IsSpawned) ReportPocketedCoinServerRpc(CoinType.White);
    }

    private void HandleQueen(Collider2D other, Vector3 pocketCenter)
    {
        SendToGraveyard(other, pocketCenter);
        if (IsSpawned) ReportPocketedCoinServerRpc(CoinType.Queen);
    }

    // -------------------------------------------------------------------------
    // GRAVEYARD — teleport off-screen instead of despawning
    // -------------------------------------------------------------------------

    private static void SendToGraveyard(Collider2D other, Vector3 pocketCenter)
    {
        SpriteRenderer sr = other.gameObject.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            // Slide ghost from coin's current position into the pocket center
            Instance.SpawnGhostCoin(sr.sprite, other.transform.position, pocketCenter + (Vector3)Instance.ghostSpawnOffset, other.transform.localScale);
            // Hide original sprite immediately to mask the Graveyard teleport streak
            sr.enabled = false;
        }

        Rigidbody2D rb = other.gameObject.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity  = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        // Encode pocket center into graveyard position so PlaybackEngine can decode it exactly
        other.transform.position = new Vector3(1000f + pocketCenter.x, 1000f + pocketCenter.y, 0f);
    }

    // -------------------------------------------------------------------------
    // GHOST COIN — physics-less visual effect, no network involvement
    // -------------------------------------------------------------------------

    // Static instance reference so the static SendToGraveyard can reach the coroutine runner
    private static BoardScript Instance;
    private void Awake() { Instance = this; }

    private void SpawnGhostCoin(Sprite originalSprite, Vector3 entryPosition, Vector3 pocketCenter, Vector3 originalScale)
    {
        if (originalSprite == null) return;
        GameObject ghost = new GameObject("GhostCoin");
        ghost.transform.position = entryPosition;
        SpriteRenderer ghostSr = ghost.AddComponent<SpriteRenderer>();
        ghostSr.sprite          = originalSprite;
        ghostSr.sortingOrder    = 10; // render on top
        StartCoroutine(AnimateGhostCoin(ghost, ghostSr, entryPosition, pocketCenter, originalScale));
    }

    private IEnumerator AnimateGhostCoin(GameObject ghost, SpriteRenderer ghostSr, Vector3 entryPosition, Vector3 pocketCenter, Vector3 originalScale)
    {
        float      duration   = 0.55f;
        float      elapsed    = 0f;
        Vector3    startScale = originalScale;
        Vector3    endScale   = Vector3.one * 0.08f;
        Color      baseColor  = ghostSr.color;
        Color      startColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.35f);
        Color      endColor   = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
        ghostSr.color         = startColor;
        Quaternion startRot   = ghost.transform.rotation;
        Quaternion endRot     = startRot * Quaternion.Euler(60f, 0f, Random.Range(-15f, 15f));

        while (elapsed < duration)
        {
            float t      = elapsed / duration;
            float easeT  = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic
            ghost.transform.position   = Vector3.Lerp(entryPosition, pocketCenter, easeT);
            ghost.transform.localScale = Vector3.Lerp(startScale, endScale, t);
            ghost.transform.rotation   = Quaternion.Slerp(startRot, endRot, easeT);
            ghostSr.color              = Color.Lerp(startColor, endColor, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(ghost);
    }

    // -------------------------------------------------------------------------
    // AI ACCESSORS
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the four pocket world positions as hardcoded constants.
    /// These match the physical corner pocket centers of the board prefab.
    /// Using hardcoded values guarantees correctness regardless of how many
    /// BoardScript instances exist in the scene or where they are placed.
    /// </summary>
    public static Vector2[] GetPocketPositions()
    {
        return new Vector2[]
        {
            new Vector2(-4.37f,  4.37f),  // Top-Left
            new Vector2( 4.37f,  4.37f),  // Top-Right
            new Vector2(-4.37f, -4.37f),  // Bottom-Left
            new Vector2( 4.37f, -4.37f),  // Bottom-Right
        };
    }

    /// <summary>
    /// Returns the baseline parameters for the given seat index.
    /// Seats 0 (South) and 2 (North) use a horizontal baseline (Y fixed, X slides).
    /// Seats 1 (East) and 3 (West) use a vertical baseline (X fixed, Y slides).
    /// Constants mirror those in StrikerController.ResetToBaseline.
    /// </summary>
    public static BaselineData GetBaseline(int seatIndex)
    {
        switch (seatIndex)
        {
            case 0: // South — horizontal, Y = -4.57f
                return new BaselineData { isHorizontal = true,  fixedAxis = -4.57f, rangeMin = -3f, rangeMax = 3f };
            case 1: // East  — vertical, X = 4.57f
                return new BaselineData { isHorizontal = false, fixedAxis =  4.57f, rangeMin = -3f, rangeMax = 3f };
            case 2: // North — horizontal, Y = 3.45f
                return new BaselineData { isHorizontal = true,  fixedAxis =  3.45f, rangeMin = -3f, rangeMax = 3f };
            case 3: // West  — vertical, X = -4.57f
                return new BaselineData { isHorizontal = false, fixedAxis = -4.57f, rangeMin = -3f, rangeMax = 3f };
            default: // fallback to seat 0
                return new BaselineData { isHorizontal = true,  fixedAxis = -4.57f, rangeMin = -3f, rangeMax = 3f };
        }
    }

    // -------------------------------------------------------------------------
    // SERVER RPCs — allow Client to write to NetworkVariables
    // -------------------------------------------------------------------------

    [ServerRpc(RequireOwnership = false)]
    private void ReportPocketedCoinServerRpc(CoinType coinType)
    {
        CarromGameManager gm = FindObjectOfType<CarromGameManager>();
        gm?.ReportPocketedCoin(coinType);
    }

    // -------------------------------------------------------------------------
    // UI — disabled pending synced VFX implementation
    // Popup text is desynced from the 800ms visual buffer, so silenced for now.
    // -------------------------------------------------------------------------

    /*
    [ClientRpc]
    private void ShowPopupTextClientRpc(string message)
    {
        StartCoroutine(textPopUp(message));
    }

    private IEnumerator textPopUp(string text)
    {
        popUpText.text = text;
        popUpText.gameObject.SetActive(true);
        yield return new WaitForSeconds(3f);
        popUpText.gameObject.SetActive(false);
    }
    */
}
