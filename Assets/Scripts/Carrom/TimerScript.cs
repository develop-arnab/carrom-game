using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;

public class TimerScript : NetworkBehaviour
{
    [SerializeField]
    TextMeshProUGUI timerText;

    public float timeLeft = 120.0f;  // The time in seconds that the timer will run for
    public bool isTimerRunning; // Indicates whether the timer is currently running
    private bool isTimerSoundPlaying = false;

    void Update()
    {
        if (isTimerRunning)
        {
            CarromGameManager gm = FindObjectOfType<CarromGameManager>();
            
            // Only Host updates the timer
            if (IsSpawned && IsServer)
            {
                timeLeft -= Time.deltaTime;
                if (gm != null)
                {
                    gm.networkTimeLeft.Value = timeLeft;
                }
            }
            else if (IsSpawned && !IsServer)
            {
                // Client reads from network variable
                if (gm != null)
                {
                    timeLeft = gm.networkTimeLeft.Value;
                }
            }
            else
            {
                // Single-player mode
                timeLeft -= Time.deltaTime;
            }
            
            // Display timer (runs on both Host and Client)
            timerText.text = Mathf.Round(timeLeft).ToString();

            if (timeLeft <= 10)
            {
                timerText.color = Color.red;
                
                if (!isTimerSoundPlaying)
                {
                    // Play the AudioSource to indicate that time is running out
                    GetComponent<AudioSource>().Play();
                    isTimerSoundPlaying = true;
                }
            }

            if (timeLeft <= 0)
            {
                // Stop the AudioSource and set the timer to not running
                GetComponent<AudioSource>().Stop();
                isTimerRunning = false;
                timerText.text = "Time's Up!";
            }
        }
    }

}
