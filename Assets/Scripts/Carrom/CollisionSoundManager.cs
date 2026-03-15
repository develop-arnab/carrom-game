using System;
using UnityEngine;

/// <summary>
/// Purely audio — plays a collision sound on coins when they hit something.
/// Also fires a static event so TelemetryRecorder can log the impact into the audio track.
/// No physics state management whatsoever.
/// </summary>
public class CollisionSoundManager : MonoBehaviour
{
    /// <summary>
    /// Fired whenever a valid coin collision sound plays.
    /// (Vector2 position, float volume)
    /// TelemetryRecorder subscribes to this to build the replay audio track.
    /// </summary>
    public static event Action<Vector2, float> OnCollisionSoundPlayed;

    private AudioSource audioSource;

    /// <summary>
    /// Fires the OnCollisionSoundPlayed event from outside this class.
    /// StrikerController calls this so its hits are also logged into the audio track.
    /// </summary>
    public static void BroadcastCollisionSound(Vector2 position, float volume)
    {
        OnCollisionSoundPlayed?.Invoke(position, volume);
    }

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        if (other.gameObject.CompareTag("Pocket")) return;
        if (other.relativeVelocity.magnitude <= 0.1f) return;

        float volume = Mathf.Clamp01(other.relativeVelocity.magnitude / 10f);
        audioSource.volume = volume;
        audioSource.Play();

        // Broadcast so TelemetryRecorder can log this event into the audio track
        OnCollisionSoundPlayed?.Invoke(transform.position, volume);
    }
}
