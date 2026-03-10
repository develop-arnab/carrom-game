using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AuthenticateUI : MonoBehaviour {


    [SerializeField] private Button authenticateButton;


    private void Awake() {
        authenticateButton.onClick.AddListener(() => {
            int randomNumber = Random.Range(1000, 10000);
            string playerName = "Player" + randomNumber;
            LobbyManager.Instance.Authenticate(playerName);
            Hide();
        });
    }

    private void Hide() {
        gameObject.SetActive(false);
    }

}