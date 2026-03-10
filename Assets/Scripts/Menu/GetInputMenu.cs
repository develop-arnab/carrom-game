using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class GetInputMenu : Panel
{

    [SerializeField] private TextMeshProUGUI messageText = null;
    [SerializeField] private TextMeshProUGUI buttonPositiveText = null;
    [SerializeField] private TextMeshProUGUI buttonNegativeText = null;
    [SerializeField] private Button positiveButton = null;
    [SerializeField] private Button negativeButton = null;
    [SerializeField] private TMP_InputField input = null;
    
    public delegate void Callback(string input);
    private Callback callback = null;
    private Type type = Type.String;
    
    public enum Type
    {
        String = 1, Integer = 2, Float = 3
    }

    public override void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }
        positiveButton.onClick.AddListener(Positive);
        negativeButton.onClick.AddListener(Negative);
        base.Initialize();
    }
    
    public void Open(Callback callback, Type type, uint maxLength, string message = "How much?", string buttonPositive = "Confirm", string buttonNegative = "Cancel")
    {
        Open();
        this.type = type;
        this.callback = callback;
        if (string.IsNullOrEmpty(message) == false)
        {
            messageText.text = message;
        }
        if (string.IsNullOrEmpty(buttonPositive) == false)
        {
            buttonPositiveText.text = buttonPositive;
        }
        if (string.IsNullOrEmpty(buttonNegative) == false)
        {
            buttonNegativeText.text = buttonNegative;
        }
        input.SetTextWithoutNotify("");
        input.characterLimit = (int)maxLength;
        switch (type)
        {
            case Type.Integer: input.contentType = TMP_InputField.ContentType.IntegerNumber; break;
            case Type.Float: input.contentType = TMP_InputField.ContentType.DecimalNumber; break;
            default: input.contentType = TMP_InputField.ContentType.Standard; break;
        }
    }
    
    private void Positive()
    {
        string value = input.text.Trim();
        if (string.IsNullOrEmpty(value) == false)
        {
            if (callback != null)
            {
                callback.Invoke(value);
            }
            Close();
        }
    }
    
    private void Negative()
    {
        Close();
    }
    
}