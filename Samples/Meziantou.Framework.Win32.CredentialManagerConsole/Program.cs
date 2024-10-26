namespace Meziantou.Framework.Win32.CredentialManagerConsole;

internal static class Program
{
    private static void Main()
    {
        CredentialResult creds;

        creds = CredentialManager.PromptForCredentialsConsole(
            target: "test",
            saveCredential: CredentialSaveOption.Hidden);
        Console.WriteLine($"User: {creds?.UserName}, Password: {creds?.Password}, Domain: {creds?.Domain}");

        creds = CredentialManager.PromptForCredentialsConsole(
            target: "https://www.meziantou.net",
            userName: "Meziantou", // Optional
            saveCredential: CredentialSaveOption.Unselected);
        Console.WriteLine($"User: {creds?.UserName}, Password: {creds?.Password}, Domain: {creds?.Domain}");

        creds = CredentialManager.PromptForCredentials(
            captionText: "https://www.meziantou.net",
            messageText: "This will allow SampleApp to authenticate to Meziantou's blog",
            //userName: "Meziantou",
            saveCredential: CredentialSaveOption.Unselected);
        Console.WriteLine($"User: {creds?.UserName}, Password: {creds?.Password}, Domain: {creds?.Domain}");
        
        creds = CredentialManager.PromptForCredentials(
            captionText: "https://www.meziantou.net",
            messageText: "Display Logon Failure message",
            //userName: "Meziantou",
            saveCredential: CredentialSaveOption.Unselected,
            error: CredentialErrorCode.LogonFailure);
        Console.WriteLine($"User: {creds?.UserName}, Password: {creds?.Password}, Domain: {creds?.Domain}");

        creds = CredentialManager.PromptForCredentials(
            captionText: "https://www.meziantou.net/dummy",
            messageText: "This will allow SampleApp to authenticate to Meziantou's blog with prefilled username",
            userName: "Meziantou",
            saveCredential: CredentialSaveOption.Unselected);
        Console.WriteLine($"User: {creds?.UserName}, Password: {creds?.Password}, Domain: {creds?.Domain}");

        creds = CredentialManager.PromptForCredentials(
            captionText: "https://www.meziantou.net/dummy",
            messageText: "This will allow SampleApp to authenticate to Meziantou's blog with prefilled username and password",
            userName: "Meziantou",
            password: "dummy",
            saveCredential: CredentialSaveOption.Unselected);
        Console.WriteLine($"User: {creds?.UserName}, Password: {creds?.Password}, Domain: {creds?.Domain}");
    }
}
