using UnityEngine;

/// <summary>
/// Drives a looping "wood sliding" sound using the unified CurrentSpeed
/// from NetworkPhysicsObject. Creates its own dedicated AudioSource at runtime
/// so it never touches the primary collision AudioSource on the prefab.
/// </summary>
[RequireComponent(typeof(NetworkPhysicsObject))]
public class ContinuousSlidingSound : MonoBehaviour
{
    [Header("Audio Clip")]
    [SerializeField] private AudioClip slidingClip;

    [Header("Speed Thresholds")]
    [Tooltip("Pieces slower than this are considered stationary — volume fades to zero.")]
    [SerializeField] private float minSpeed = 0.3f;

    [Tooltip("Speed at which volume reaches its maximum. Should match ~peak striker velocity.")]
    [SerializeField] private float maxSpeed = 20f;

    [Header("Volume")]
    [SerializeField] private float maxVolume = 1f;

    [Tooltip("How quickly volume tracks speed changes. Higher = snappier, lower = smoother.")]
    [SerializeField] private float volumeLerpSpeed = 10f;

    [Header("Pitch")]
    [SerializeField] private float minPitch = 0.8f;
    [SerializeField] private float maxPitch = 1.6f;

    [Tooltip("How quickly pitch tracks speed changes.")]
    [SerializeField] private float pitchLerpSpeed = 6f;

    private AudioSource          slidingSource;
    private NetworkPhysicsObject npo;

    // -------------------------------------------------------------------------
    // LIFECYCLE
    // -------------------------------------------------------------------------

    private void Awake()
    {
        npo = GetComponent<NetworkPhysicsObject>();

        // Spawn a dedicated AudioSource so we never touch the collision one
        slidingSource             = gameObject.AddComponent<AudioSource>();
        slidingSource.clip        = slidingClip;
        slidingSource.loop        = true;
        slidingSource.playOnAwake = false;
        slidingSource.volume      = 0f;
        slidingSource.Play();
    }

    // -------------------------------------------------------------------------
    // AUDIO MODULATION
    // -------------------------------------------------------------------------

    private void Update()
    {
        float speed = npo.CurrentSpeed;

        float targetVolume = speed < minSpeed
            ? 0f
            : Mathf.Clamp01((speed - minSpeed) / (maxSpeed - minSpeed)) * maxVolume;

        float targetPitch = Mathf.Lerp(minPitch, maxPitch, Mathf.Clamp01(speed / maxSpeed));

        slidingSource.volume = Mathf.Lerp(slidingSource.volume, targetVolume, Time.deltaTime * volumeLerpSpeed);
        slidingSource.pitch  = Mathf.Lerp(slidingSource.pitch,  targetPitch,  Time.deltaTime * pitchLerpSpeed);
    }
}
