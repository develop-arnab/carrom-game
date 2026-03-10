using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models.Data.Player;
using UnityEngine.UI;
using SaveOptions = Unity.Services.CloudSave.Models.Data.Player.SaveOptions;

public class CustomizationMenu : Panel
{

    [SerializeField] public TextMeshProUGUI characterText = null;
    [SerializeField] private Button characterButton = null;
    [SerializeField] private Button colorButton = null;
    [SerializeField] private Button closeButton = null;
    [SerializeField] private Button saveButton = null;
    
    private int savedColor = 0;
    private int savedCharacter = 0;
    
    private int color = 0;
    private int character = 0;

    private string[] characters = { "Cube", "Capsule", "Sphere" };
    private Color[] colors = { Color.green, Color.red, Color.blue, Color.magenta, Color.cyan };

    public override void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }
        closeButton.onClick.AddListener(ClosePanel);
        characterButton.onClick.AddListener(ChangeCharacter);
        colorButton.onClick.AddListener(ChangeColor);
        saveButton.onClick.AddListener(Save);
        base.Initialize();
    }
    
    public override void Open()
    {
        base.Open();
        LoadData();
    }
    
    private async void LoadData()
    {
        characterText.text = "";
        characterButton.interactable = false;
        colorButton.interactable = false;
        saveButton.interactable = false;
        character = 0;
        color = 0;
        savedCharacter = 0;
        savedColor = 0;
        try
        {
            var playerData = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { "character" }, new LoadOptions(new PublicReadAccessClassOptions()));
            if (playerData.TryGetValue("character", out var characterData))
            {
                var data = characterData.Value.GetAs<Dictionary<string, object>>();
                savedCharacter = int.Parse(data["type"].ToString());
                savedColor = int.Parse(data["color_index"].ToString());
                character = savedCharacter;
                color = savedColor;
            }
        }
        catch (Exception exception)
        {
            Debug.Log(exception.Message);
        }
        characterButton.interactable = true;
        colorButton.interactable = true;
        ApplyData();
    }

    private async void Save()
    {
        saveButton.interactable = false;
        characterButton.interactable = false;
        colorButton.interactable = false;
        try
        {
            var playerData = new Dictionary<string, object>
            {
                { "type", character },
                { "color", "#" + ColorUtility.ToHtmlStringRGBA(colors[color]) },
                { "color_index", color }
            };
            var data = new Dictionary<string, object> { { "character", playerData } };
            await CloudSaveService.Instance.Data.Player.SaveAsync(data, new SaveOptions(new PublicWriteAccessClassOptions()));
            savedCharacter = character;
            savedColor = color;
        }
        catch (Exception exception)
        {
            Debug.Log(exception.Message);
            saveButton.interactable = true;
        }
        characterButton.interactable = true;
        colorButton.interactable = true;
    }

    private void ChangeCharacter()
    {
        character++;
        if (character >= characters.Length)
        {
            character = 0;
        }
        ApplyData();
    }

    private void ChangeColor()
    {
        color++;
        if (color >= colors.Length)
        {
            color = 0;
        }
        ApplyData();
    }
    
    private void ApplyData()
    {
        characterText.text = characters[character];
        characterText.color = colors[color];
        saveButton.interactable = character != savedCharacter || color != savedColor;
    }
    
    private void ClosePanel()
    {
        Close();
    }
    
}