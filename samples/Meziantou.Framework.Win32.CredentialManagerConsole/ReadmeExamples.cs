namespace Meziantou.Framework.Win32.CredentialManagerConsole;

// Example of the readme (src/Meziantou.Framework.Win32.CredentialManager/readme.md). It changes the credentials of the
// user, so it is compiled in CI to keep the documentation in sync with the API, but not run.
public static class ReadmeExamples
{
    public static void Usage()
    {
        #region CredentialManager_Usage
        // Save the credential to the credential manager
        CredentialManager.WriteCredential(
            applicationName: "CredentialManagerTests",
            userName: "meziantou",
            secret: "Pa$$w0rd",
            comment: "Test",
            persistence: CredentialPersistence.LocalMachine);

        // Get a credential from the credential manager.
        // ReadCredential returns null when no such credential exists.
        var cred = CredentialManager.ReadCredential(applicationName: "CredentialManagerTests");
        if (cred is not null)
        {
            Console.WriteLine(cred.UserName);
            Console.WriteLine(cred.Password);
        }

        // Delete a credential from the credential manager.
        // DeleteCredential throws Win32Exception when no such credential exists.
        CredentialManager.DeleteCredential(applicationName: "CredentialManagerTests");
        #endregion
    }
}
