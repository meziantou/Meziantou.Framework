using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The credential store of the Credential Management API: the credentials stored by the test, chosen without asking (the
// first stored credential that matches the request; a silent request returns the only one)
internal sealed partial class WptUserAgentUI
{
    private readonly List<Credential> _credentials = [];

    public override ValueTask<Credential?> ChooseCredentialAsync(CredentialChooserPrompt prompt, CancellationToken cancellationToken)
    {
        lock (_credentials)
        {
            var matching = _credentials.FindAll(prompt.Matches);
            return new(prompt.Mediation == CredentialMediationRequirement.Silent && matching.Count != 1 ? null : matching.FirstOrDefault());
        }
    }

    public override ValueTask<bool> StoreCredentialAsync(CredentialStorePrompt prompt, CancellationToken cancellationToken)
    {
        lock (_credentials)
        {
            _credentials.RemoveAll(credential => credential.GetType() == prompt.Credential.GetType() && credential.Id == prompt.Credential.Id && GetOrigin(credential) == GetOrigin(prompt.Credential));
            _credentials.Add(prompt.Credential);
        }

        return new(true);
    }

    private static string? GetOrigin(Credential credential) => credential switch
    {
        PasswordCredential password => password.Origin,
        FederatedCredential federated => federated.Origin,
        _ => null,
    };
}
