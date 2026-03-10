using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;

public class BoardScript : NetworkBehaviour
{
    public static int scoreEnemy = 0;
    public static int scorePlayer = 0;

    TextMeshProUGUI popUpText;

    private void Start()
    {
        // Find the UpdatesText object and get the TextMeshProUGUI component
        popUpText = GameObject.Find("UpdatesText").GetComponent<TextMeshProUGUI>();
    }

    IEnumerator textPopUp(string text)
    {
        // Set the text and activate the UpdatesText object
        popUpText.text = text;
        popUpText.gameObject.SetActive(true);
        yield return new WaitForSeconds(3f);
        // Deactivate the UpdatesText object after 3 seconds
        popUpText.gameObject.SetActive(false);
    }
    [ClientRpc]
    private void ShowPopupTextClientRpc(string message)
    {
        StartCoroutine(textPopUp(message));
    }


    private void OnTriggerEnter2D(Collider2D other)
    {
        // Only Host processes collisions in multiplayer
        if (IsSpawned && !IsServer)
        {
            return;
        }
        
        // Play audio when a coin/striker enters the pocket
        GetComponent<AudioSource>().Play();
        
        CarromGameManager gm = FindObjectOfType<CarromGameManager>();

        switch (other.gameObject.tag)
        {
            case "Striker":
                if (StrikerController.playerTurn == true)
                {
                    scorePlayer--; // Decrement the player's score by 1
                    if (IsSpawned && gm != null)
                    {
                        gm.networkScorePlayer.Value--;
                    }
                }
                else
                {
                    scoreEnemy--; // Decrement the enemy's score by 1
                    if (IsSpawned && gm != null)
                    {
                        gm.networkScoreEnemy.Value--;
                    }
                }

                string strikerMessage = "Striker Lost! -1 to " + (StrikerController.playerTurn ? "Player" : "Enemy");
                if (IsSpawned)
                {
                    ShowPopupTextClientRpc(strikerMessage);
                }
                else
                {
                    StartCoroutine(textPopUp(strikerMessage));
                }
                
                other.gameObject.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero; // Set the velocity of the Striker to zero
                break;

            case "Black":
                scoreEnemy++; // Increment the enemy's score by 1
                if (IsSpawned && gm != null)
                {
                    gm.networkScoreEnemy.Value++;
                }

                string blackMessage = "Black Coin Entered! +1 to Enemy";
                if (IsSpawned)
                {
                    ShowPopupTextClientRpc(blackMessage);
                }
                else
                {
                    StartCoroutine(textPopUp(blackMessage));
                }
                
                // Properly destroy NetworkObject
                if (IsSpawned)
                {
                    NetworkObject netObj = other.gameObject.GetComponent<NetworkObject>();
                    if (netObj != null)
                    {
                        netObj.Despawn(true);
                    }
                }
                else
                {
                    Destroy(other.gameObject);
                }
                break;

            case "White":
                scorePlayer++; // Increment the player's score by 1
                if (IsSpawned && gm != null)
                {
                    gm.networkScorePlayer.Value++;
                }

                string whiteMessage = "White Coin Entered! +1 to Player";
                if (IsSpawned)
                {
                    ShowPopupTextClientRpc(whiteMessage);
                }
                else
                {
                    StartCoroutine(textPopUp(whiteMessage));
                }
                
                // Properly destroy NetworkObject
                if (IsSpawned)
                {
                    NetworkObject netObj = other.gameObject.GetComponent<NetworkObject>();
                    if (netObj != null)
                    {
                        netObj.Despawn(true);
                    }
                }
                else
                {
                    Destroy(other.gameObject);
                }
                break;

            case "Queen":
                if (StrikerController.playerTurn == true)
                {
                    scorePlayer += 2; // Increment the player's score by 2
                    if (IsSpawned && gm != null)
                    {
                        gm.networkScorePlayer.Value += 2;
                    }
                }
                else
                {
                    scoreEnemy += 2; // Increment the enemy's score by 2
                    if (IsSpawned && gm != null)
                    {
                        gm.networkScoreEnemy.Value += 2;
                    }
                }

                string queenMessage = "Queen Entered! +2 to " + (StrikerController.playerTurn ? "Player" : "Enemy");
                if (IsSpawned)
                {
                    ShowPopupTextClientRpc(queenMessage);
                }
                else
                {
                    StartCoroutine(textPopUp(queenMessage));
                }
                
                // Properly destroy NetworkObject
                if (IsSpawned)
                {
                    NetworkObject netObj = other.gameObject.GetComponent<NetworkObject>();
                    if (netObj != null)
                    {
                        netObj.Despawn(true);
                    }
                }
                else
                {
                    Destroy(other.gameObject);
                }
                break;
        }
    }
}
