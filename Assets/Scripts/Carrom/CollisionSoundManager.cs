using UnityEngine;

/// <summary>
/// Purely audio — plays a collision sound on coins when they hit something.
/// No physics state management. Physics body types are owned exclusively
/// by BatchTransmitter (spectator replay) and the active player's Rigidbody2D.
/// </summary>
public class CollisionSoundManager : MonoBehaviour
{
    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        if (other.gameObject.CompareTag("Pocket")) return;
        if (other.relativeVelocity.magnitude <= 0.1f) return;

        audioSource.volume = Mathf.Clamp01(other.relativeVelocity.magnitude / 10f);
        audioSource.Play();
    }
}
