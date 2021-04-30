# Meziantou.Framework.Win32.CredentialManager

````c#
// Save the credential to the credential manager
CredentialManager.WriteCredential(
    applicationName: "CredentialManagerTests",
    userName: "meziantou",
    secret: "Pa$$w0rd",
    comment: "Test",
    persistence: CredentialPersistence.LocalMachine);

// Get a credential from the credential manager
var cred = CredentialManager.ReadCredential(applicationName: "CredentialManagerTests");
Console.WriteLine(cred.UserName);
Console.WriteLine(cred.Password);

// Delete a credential from the credential manager
CredentialManager.DeleteCredential(applicationName: "CredentialManagerTests");
````

## Additional resources

- [How to store a password on Windows?](https://www.meziantou.net/how-to-store-a-password-on-windows.htm#windows-desktop-appl)