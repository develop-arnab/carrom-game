using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plug-and-play local UI button sound.
/// Attach to any UI Button GameObject and assign a clip in the Inspector.
///
/// NETWORK SAFETY: Inherits from MonoBehaviour — never synced across the network.
/// Each player hears only their own clicks.
/// </summary>
[RequireComponent(typeof(Button))]
public class LocalUIButtonSound : MonoBehaviour
{
    [SerializeField]
    [Tooltip("The sound to play when this button is clicked.")]
    private AudioClip buttonClickSound;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(PlayLocalSound);
    }

    private void OnDestroy()
    {
        // Unregister to prevent memory leaks across scene transitions
        if (_button != null)
            _button.onClick.RemoveListener(PlayLocalSound);
    }

    private void PlayLocalSound()
    {
        if (buttonClickSound == null) return;
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySoundEffect(buttonClickSound);
    }
}
