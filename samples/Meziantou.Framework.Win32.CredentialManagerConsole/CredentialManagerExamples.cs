namespace Meziantou.Framework.Win32.CredentialManagerConsole;

// Examples of the documentation (docs/Win32). They read or change the credentials of the user, or show a prompt,
// so they are compiled in CI to keep the documentation in sync with the API, but not run.
public static class CredentialManagerExamples
{
    public static void Enumerate()
    {
        #region CredentialManager_Enumerate
        // The filter supports wildcards: list the credentials whose name starts with "MyApp:"
        foreach (var credential in CredentialManager.EnumerateCredentials("MyApp:*"))
        {
            Console.WriteLine($"{credential.ApplicationName}: {credential.UserName} ({credential.CredentialType})");
        }
        #endregion
    }

    public static void TryDelete()
    {
        #region CredentialManager_TryDelete
        // Returns false instead of throwing when the credential does not exist
        if (!CredentialManager.TryDeleteCredential("MyApp:https://api.example.com"))
        {
            Console.WriteLine("The credential does not exist");
        }
        #endregion
    }

    public static void Prompt()
    {
        #region CredentialManager_Prompt
        // Show the Windows credential dialog
        var result = CredentialManager.PromptForCredentials(
            captionText: "MyApp",
            messageText: "Enter your credentials for https://api.example.com",
            userName: Environment.UserName,
            saveCredential: CredentialSaveOption.Unselected);

        // null when the user cancels the dialog
        if (result is not null)
        {
            if (result.CredentialSaved is CredentialSaveOption.Selected)
            {
                // The user checked "Remember my credentials": storing them is up to the application
                CredentialManager.WriteCredential("MyApp:https://api.example.com", result.UserName, result.Password, CredentialPersistence.LocalMachine);
            }
        }
        #endregion
    }

    public static void PromptConsole()
    {
        #region CredentialManager_PromptConsole
        // Ask for the credentials in the console, without the checkbox to save them
        var result = CredentialManager.PromptForCredentialsConsole(
            target: "https://api.example.com",
            saveCredential: CredentialSaveOption.Hidden);

        if (result is not null)
        {
            Console.WriteLine($"Hello {result.UserName}");
        }
        #endregion
    }
}
