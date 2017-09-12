namespace Meziantou.Framework.Win32
{
    public class Credential
    {
        public CredentialType CredentialType { get; }
        public string ApplicationName { get; }
        public string UserName { get; }
        public string Password { get; }

        public Credential(CredentialType credentialType, string applicationName, string userName, string password)
        {
            ApplicationName = applicationName;
            UserName = userName;
            Password = password;
            CredentialType = credentialType;
        }

        public override string ToString()
        {
            return $"CredentialType: {CredentialType}, ApplicationName: {ApplicationName}, UserName: {UserName}, Password: {Password}";
        }
    }
}