using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class AuthenticationMenu : Panel
{

    [SerializeField] private TMP_InputField usernameInput = null;
    [SerializeField] private TMP_InputField passwordInput = null;
    [SerializeField] private Button signinButton = null;
    [SerializeField] private Button signupButton = null;
    [SerializeField] private Button anonymousButton = null;
    [SerializeField] private TextMeshProUGUI errorText = null;

    public override void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }
        anonymousButton.onClick.AddListener(AnonymousSignIn);
        signinButton.onClick.AddListener(SignIn);
        signupButton.onClick.AddListener(SignIn); // both buttons use the same unified flow
        base.Initialize();
    }

    public override void Open()
    {
        usernameInput.text = "";
        passwordInput.text = "";
        ClearError();
        base.Open();
    }

    public void ShowError(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
            errorText.gameObject.SetActive(true);
        }
    }

    public void ClearError()
    {
        if (errorText != null)
        {
            errorText.text = "";
            errorText.gameObject.SetActive(false);
        }
    }

    private void AnonymousSignIn()
    {
        ClearError();
        MainMenuManager.Singleton.SignInAnonymouslyAsync();
    }

    private void SignIn()
    {
        ClearError();
        string user = usernameInput.text.Trim();
        string pass = passwordInput.text.Trim();
        if (string.IsNullOrEmpty(user) == false && string.IsNullOrEmpty(pass) == false)
        {
            MainMenuManager.Singleton.SignInWithUsernameAndPasswordAsync(user, pass);
        }
    }

    private void SignUp()
    {
        ClearError();
        string user = usernameInput.text.Trim();
        string pass = passwordInput.text.Trim();
        if (string.IsNullOrEmpty(user) == false && string.IsNullOrEmpty(pass) == false)
        {
            if (IsPasswordValid(pass))
            {
                MainMenuManager.Singleton.SignUpWithUsernameAndPasswordAsync(user, pass);
            }
            else
            {
                ShowError("Password needs 8-30 chars with uppercase, lowercase, digit and symbol.");
            }
        }
    }
    
    private bool IsPasswordValid(string password)
    {
        if (password.Length < 8 || password.Length > 30)
        {
            return false;
        }
        
        bool hasUppercase = false;
        bool hasLowercase = false;
        bool hasDigit = false;
        bool hasSymbol = false;

        foreach (char c in password)
        {
            if (char.IsUpper(c))
            {
                hasUppercase = true;
            }
            else if (char.IsLower(c))
            {
                hasLowercase = true;
            }
            else if (char.IsDigit(c))
            {
                hasDigit = true;
            }
            else if (!char.IsLetterOrDigit(c))
            {
                hasSymbol = true;
            }
        }
        return hasUppercase && hasLowercase && hasDigit && hasSymbol;
    }
    
}