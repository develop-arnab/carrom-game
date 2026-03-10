using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LocalCharacterSelection : MonoBehaviour
{
[Header("Character data")]
public CharacterDataSO[] charactersData; // assign same SO array used by CharacterSelectionManager
[Header("UI")]
public Image characterImage;
public TextMeshProUGUI characterNameText;
public Image shipImage;
public AudioClip changeClip;

private int m_index = 0;

private void Start()
{
    if (charactersData == null || charactersData.Length == 0)
    {
        Debug.LogWarning("LocalCharacterSelection: no characters assigned.");
        return;
    }

    m_index = Mathf.Clamp(m_index, 0, charactersData.Length - 1);
    RefreshUI();
}

public void Next()
{
    m_index++;
    if (m_index >= charactersData.Length) m_index = 0;
    RefreshUI();
    PlayChangeSfx();
}

public void Prev()
{
    m_index--;
    if (m_index < 0) m_index = charactersData.Length - 1;
    RefreshUI();
    PlayChangeSfx();
}

private void RefreshUI()
{
    var data = charactersData[m_index];
    if (characterImage != null) characterImage.sprite = data.characterSprite;
    if (characterNameText != null) characterNameText.text = data.characterName;
    if (shipImage != null) shipImage.sprite = data.characterShipSprite;
}

private void PlayChangeSfx()
{
    if (changeClip == null) return;
    if (AudioManager.Instance != null)
        AudioManager.Instance.PlaySoundEffect(changeClip);
}

// Optional: expose index getter for when you transition to the networked selection scene
public int GetSelectedIndex() => m_index;
}