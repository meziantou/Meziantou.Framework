#nullable disable
namespace Meziantou.Framework.Win32
{
    public class CredentialResult
    {
        public CredentialResult(string userName, string password, string domain, CredentialSaveOption credentialSaved)
        {
            UserName = userName;
            Password = password;
            Domain = domain;
            CredentialSaved = credentialSaved;
        }

        public string UserName { get; }
        public string Password { get; }
        public string Domain { get; }
        public CredentialSaveOption CredentialSaved { get; }
    }
}
