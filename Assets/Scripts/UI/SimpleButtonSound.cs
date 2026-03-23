using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class SimpleButtonSound : MonoBehaviour
{
    [SerializeField] private AudioClip clickSound;

    private void Awake()
    {
        // Hook up the click event automatically
        GetComponent<Button>().onClick.AddListener(PlaySound);
    }

    private void PlaySound()
    {
        if (clickSound != null)
        {
            // 1. Create a temporary, invisible GameObject in the scene root
            GameObject tempAudioObj = new GameObject("TempUI_Speaker");
            
            // 2. Add an AudioSource to it
            AudioSource tempSource = tempAudioObj.AddComponent<AudioSource>();
            
            // 3. Configure it for 2D UI sound
            tempSource.spatialBlend = 0f; 
            tempSource.clip = clickSound;
            
            // 4. Play the sound
            tempSource.Play();
            
            // 5. Tell Unity to destroy this temporary speaker exactly when the sound finishes playing
            Destroy(tempAudioObj, clickSound.length);
        }
    }

    private void OnDestroy()
    {
        // Clean up the listener if the button is permanently destroyed
        GetComponent<Button>().onClick.RemoveListener(PlaySound);
    }
}