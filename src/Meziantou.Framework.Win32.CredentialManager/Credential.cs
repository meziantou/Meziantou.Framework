namespace Meziantou.Framework.Win32
{
    public class Credential
    {
        public CredentialType CredentialType { get; }
        public string ApplicationName { get; }
        public string UserName { get; }
        public string Password { get; }
        public string Comment { get; }

        public Credential(CredentialType credentialType, string applicationName, string userName, string password, string comment)
        {
            ApplicationName = applicationName;
            UserName = userName;
            Password = password;
            CredentialType = credentialType;
            Comment = comment;
        }

        public override string ToString()
        {
            return $"CredentialType: {CredentialType}, ApplicationName: {ApplicationName}, UserName: {UserName}, Password: {Password}, Comment: {Comment}";
        }
    }
}