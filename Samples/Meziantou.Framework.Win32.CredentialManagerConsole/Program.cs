using System;
using System.Net;

namespace Meziantou.Framework.Win32.CredentialManagerConsole
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            NetworkCredential creds;

            creds = CredentialManager.PromptForCredentialsConsole("Test", saveCredential: false);
            Console.WriteLine($"User: {creds?.UserName}, Password: {creds?.Password}, Domain: {creds?.Domain}");

            creds = CredentialManager.PromptForCredentials("Test", captionText: "My Caption", messageText: "My message", userName: "My Username", saveCredential: true);
            Console.WriteLine($"User: {creds?.UserName}, Password: {creds?.Password}, Domain: {creds?.Domain}");
        }
    }
}
