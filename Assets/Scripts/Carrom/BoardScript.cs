using System.Collections;
using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Handles pocket triggers using the Graveyard pattern.
///
/// Key changes from original:
/// - Owner check replaces IsServer guard so the active Client processes their own triggers.
/// - Despawn/Destroy replaced with off-screen teleport so PlaybackEngine replay is never broken.
/// - Score writes go through ServerRpcs so the Client can update NetworkVariables safely.
/// </summary>
public class BoardScript : NetworkBehaviour
{
    public static int scoreEnemy  = 0;
    public static int scorePlayer = 0;

    // Off-screen graveyard position — far enough to never interfere with physics
    private static readonly Vector3 GraveyardPosition = new Vector3(1000f, 1000f, 0f);

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
                HandleBlack(other);
                break;
            case "White":
                HandleWhite(other);
                break;
            case "Queen":
                HandleQueen(other);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // POCKET HANDLERS
    // -------------------------------------------------------------------------

    private void HandleStriker(Collider2D other)
    {
        other.gameObject.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;

        bool isPlayerTurn = StrikerController.playerTurn;
        string msg = "Striker Lost! -1 to " + (isPlayerTurn ? "Player" : "Enemy");

        if (IsSpawned)
        {
            if (isPlayerTurn) AddPlayerScoreServerRpc(-1);
            else              AddEnemyScoreServerRpc(-1);
            ShowPopupTextClientRpc(msg);
        }
        else
        {
            if (isPlayerTurn) scorePlayer--;
            else              scoreEnemy--;
            StartCoroutine(textPopUp(msg));
        }
    }

    private void HandleBlack(Collider2D other)
    {
        SendToGraveyard(other);

        string msg = "Black Coin Entered! +1 to Enemy";
        if (IsSpawned)
        {
            AddEnemyScoreServerRpc(1);
            ShowPopupTextClientRpc(msg);
        }
        else
        {
            scoreEnemy++;
            StartCoroutine(textPopUp(msg));
        }
    }

    private void HandleWhite(Collider2D other)
    {
        SendToGraveyard(other);

        string msg = "White Coin Entered! +1 to Player";
        if (IsSpawned)
        {
            AddPlayerScoreServerRpc(1);
            ShowPopupTextClientRpc(msg);
        }
        else
        {
            scorePlayer++;
            StartCoroutine(textPopUp(msg));
        }
    }

    private void HandleQueen(Collider2D other)
    {
        SendToGraveyard(other);

        bool isPlayerTurn = StrikerController.playerTurn;
        string msg = "Queen Entered! +2 to " + (isPlayerTurn ? "Player" : "Enemy");

        if (IsSpawned)
        {
            if (isPlayerTurn) AddPlayerScoreServerRpc(2);
            else              AddEnemyScoreServerRpc(2);
            ShowPopupTextClientRpc(msg);
        }
        else
        {
            if (isPlayerTurn) scorePlayer += 2;
            else              scoreEnemy  += 2;
            StartCoroutine(textPopUp(msg));
        }
    }

    // -------------------------------------------------------------------------
    // GRAVEYARD — teleport off-screen instead of despawning
    // -------------------------------------------------------------------------

    private static void SendToGraveyard(Collider2D other)
    {
        // Grab sprite before teleporting so the ghost spawns at the pocket position
        SpriteRenderer sr = other.gameObject.GetComponent<SpriteRenderer>();
        if (sr != null) Instance.SpawnGhostCoin(sr.sprite, other.transform.position);

        Rigidbody2D rb = other.gameObject.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity  = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        other.transform.position = GraveyardPosition;
    }

    // -------------------------------------------------------------------------
    // GHOST COIN — physics-less visual effect, no network involvement
    // -------------------------------------------------------------------------

    // Static instance reference so the static SendToGraveyard can reach the coroutine runner
    private static BoardScript Instance;
    private void Awake() { Instance = this; }

    private void SpawnGhostCoin(Sprite originalSprite, Vector3 spawnPosition)
    {
        if (originalSprite == null) return;
        GameObject ghost = new GameObject("GhostCoin");
        ghost.transform.position = spawnPosition;
        SpriteRenderer ghostSr = ghost.AddComponent<SpriteRenderer>();
        ghostSr.sprite          = originalSprite;
        ghostSr.sortingOrder    = 10; // render on top
        StartCoroutine(AnimateGhostCoin(ghost, ghostSr));
    }

    private IEnumerator AnimateGhostCoin(GameObject ghost, SpriteRenderer ghostSr)
    {
        float   duration   = 0.6f;
        float   elapsed    = 0f;
        Vector3 startScale = Vector3.one * 0.7f;
        Vector3 endScale   = Vector3.one * 0.4f;
        Color   baseColor  = ghostSr.color;
        Color   startColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.6f);
        Color   endColor   = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
        ghostSr.color      = startColor;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            ghost.transform.localScale = Vector3.Lerp(startScale, endScale, t);
            ghostSr.color              = Color.Lerp(startColor, endColor, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(ghost);
    }

    // -------------------------------------------------------------------------
    // SERVER RPCs — allow Client to write to NetworkVariables
    // -------------------------------------------------------------------------

    [ServerRpc(RequireOwnership = false)]
    private void AddPlayerScoreServerRpc(int amount)
    {
        CarromGameManager gm = FindObjectOfType<CarromGameManager>();
        if (gm != null) gm.networkScorePlayer.Value += amount;
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddEnemyScoreServerRpc(int amount)
    {
        CarromGameManager gm = FindObjectOfType<CarromGameManager>();
        if (gm != null) gm.networkScoreEnemy.Value += amount;
    }

    // -------------------------------------------------------------------------
    // UI
    // -------------------------------------------------------------------------

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
}
