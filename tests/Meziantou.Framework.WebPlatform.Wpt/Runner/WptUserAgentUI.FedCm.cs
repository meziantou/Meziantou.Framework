using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The dialogs of FedCM (the account chooser, the confirm IDP login dialog, the error dialog), driven by the WebDriver extension
// commands of FedCM (https://w3c-fedid.github.io/FedCM/#automation): a dialog stays open until a command answers it. The
// auto reauthentication notice is not shown (it continues at once), and a cooldown dismisses the account chooser and the
// confirm IDP login dialog once the user canceled one, until reset_fedcm_cooldown. The library does not delay the
// rejections, so set_fedcm_delay_enabled has no effect.
internal sealed partial class WptUserAgentUI
{
    private FedCmDialog? _fedCmDialog;
    private bool _fedCmCooldown;

    public override ValueTask<IdentityAccountChoice?> ChooseIdentityAccountAsync(IdentityAccountChooserPrompt prompt, CancellationToken cancellationToken)
    {
        if (_fedCmCooldown)
            return new((IdentityAccountChoice?)null);

        var completion = new TaskCompletionSource<IdentityAccountChoice?>();
        ShowFedCmDialog(new FedCmDialog("AccountChooser", prompt, prompt.Accounts)
        {
            Cancel = () =>
            {
                _fedCmCooldown = true;
                completion.TrySetResult(null);
            },
            Abort = () => completion.TrySetCanceled(),
            SelectAccount = index => completion.TrySetResult(IdentityAccountChoice.Select(prompt.Accounts[index])),
            ClickButton = button => button == "ConfirmIdpLoginContinue" && prompt.SupportsUseOtherAccount && completion.TrySetResult(IdentityAccountChoice.UseOtherAccount),
        }, completion.Task, cancellationToken);
        return new(completion.Task);
    }

    public override ValueTask<bool> ConfirmIdentityProviderLoginAsync(IdentityProviderLoginPrompt prompt, CancellationToken cancellationToken)
    {
        if (_fedCmCooldown)
            return new(false);

        var completion = new TaskCompletionSource<bool>();
        ShowFedCmDialog(new FedCmDialog("ConfirmIdpLogin", prompt, [])
        {
            Cancel = () =>
            {
                _fedCmCooldown = true;
                completion.TrySetResult(false);
            },
            Abort = () => completion.TrySetCanceled(),
            ClickButton = button => button == "ConfirmIdpLoginContinue" && completion.TrySetResult(true),
        }, completion.Task, cancellationToken);
        return new(completion.Task);
    }

    public override ValueTask ShowIdentityCredentialErrorAsync(IdentityCredentialErrorPrompt prompt, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>();
        ShowFedCmDialog(new FedCmDialog("Error", prompt, [])
        {
            Cancel = () => completion.TrySetResult(false),
            Abort = () => completion.TrySetCanceled(),
            ClickButton = button => button is "ErrorGotIt" or "ErrorMoreDetails" && completion.TrySetResult(true),
        }, completion.Task, cancellationToken);
        return new(completion.Task);
    }

    /// <summary>Shows a dialog until it is answered or its request is aborted.</summary>
    private void ShowFedCmDialog<T>(FedCmDialog dialog, Task<T> answer, CancellationToken cancellationToken)
    {
        _fedCmDialog = dialog;
        var registration = cancellationToken.Register(() => dialog.Abort?.Invoke());
        _ = answer.ContinueWith(_ =>
        {
            registration.Dispose();
            Interlocked.CompareExchange(ref _fedCmDialog, null, dialog);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private bool TryExecuteFedCmCommand(string command, JsonArray arguments, out string? result)
    {
        result = null;
        switch (command)
        {
            case "set_fedcm_delay_enabled":
                if (arguments is not [{ } enabled] || enabled.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                    throw WptWebDriverException.InvalidArgument("enabled is not a boolean");

                return true;

            case "reset_fedcm_cooldown":
                _fedCmCooldown = false;
                return true;

            case "get_fedcm_dialog_type":
                result = JsonSerializer.Serialize(GetFedCmDialog().Type);
                return true;

            case "get_fedcm_dialog_title":
                var (title, subtitle) = GetFedCmDialog().GetTitle();
                var titleObject = new JsonObject { ["title"] = title };
                if (subtitle is not null)
                {
                    titleObject["subtitle"] = subtitle;
                }

                result = titleObject.ToJsonString();
                return true;

            case "get_fedcm_account_list":
                var accounts = new JsonArray();
                foreach (var account in GetFedCmDialog().Accounts)
                {
                    var item = new JsonObject
                    {
                        ["accountId"] = account.Id,
                        ["email"] = account.Email,
                        ["name"] = account.Name,
                        ["idpConfigUrl"] = account.ConfigUrl,
                        ["loginState"] = account.IsConnected ? "SignIn" : "SignUp",
                    };
                    AddIfNotNull(item, "givenName", account.GivenName);
                    AddIfNotNull(item, "pictureUrl", account.Picture);
                    AddIfNotNull(item, "termsOfServiceUrl", account.TermsOfServiceUrl);
                    AddIfNotNull(item, "privacyPolicyUrl", account.PrivacyPolicyUrl);
                    accounts.Add(item);
                }

                result = accounts.ToJsonString();
                return true;

            case "select_fedcm_account":
                var dialog = GetFedCmDialog();
                if (arguments is not [{ } indexNode] || indexNode.GetValueKind() != JsonValueKind.Number || !indexNode.AsValue().TryGetValue<int>(out var index) || index < 0 || index >= dialog.Accounts.Count || dialog.SelectAccount is not { } selectAccount)
                    throw WptWebDriverException.InvalidArgument("the account index is not valid");

                selectAccount(index);
                return true;

            case "cancel_fedcm_dialog":
                GetFedCmDialog().Cancel?.Invoke();
                return true;

            case "click_fedcm_dialog_button":
                var button = GetString(arguments.Count > 0 ? arguments[0] : null, "dialogButton");
                if (button is not ("ConfirmIdpLoginContinue" or "ErrorGotIt" or "ErrorMoreDetails"))
                    throw WptWebDriverException.InvalidArgument("the dialog button is not valid");

                if (GetFedCmDialog().ClickButton?.Invoke(button) != true)
                    throw new WptWebDriverException("no such alert");

                return true;

            default:
                return false;
        }

        static void AddIfNotNull(JsonObject item, string name, string? value)
        {
            if (value is not null)
            {
                item[name] = value;
            }
        }
    }

    private FedCmDialog GetFedCmDialog() => _fedCmDialog ?? throw new WptWebDriverException("no such alert");

    /// <summary>An open dialog of FedCM: its type (as get_fedcm_dialog_type returns it), its accounts, and the actions of the commands.</summary>
    private sealed class FedCmDialog(string type, IdentityCredentialPrompt prompt, IReadOnlyList<IdentityProviderAccount> accounts)
    {
        public string Type { get; } = type;

        public IReadOnlyList<IdentityProviderAccount> Accounts { get; } = accounts;

        /// <summary>The user cancels the dialog.</summary>
        public Action? Cancel { get; init; }

        /// <summary>The request of the dialog is aborted (e.g. replaced by another request): the dialog is closed, without cooldown.</summary>
        public Action? Abort { get; init; }

        public Action<int>? SelectAccount { get; init; }

        /// <summary>Clicks a button of the dialog: false when the dialog has no such button.</summary>
        public Func<string, bool>? ClickButton { get; init; }

        /// <summary>The title of the dialog (e.g. "Sign in to web-platform.test with not-web-platform.test"), and the site of the requesting frame when it is not the top-level site.</summary>
        public (string Title, string? Subtitle) GetTitle()
        {
            var topLevel = GetHost(prompt.TopLevelOrigin);
            var configUrls = prompt switch
            {
                IdentityAccountChooserPrompt chooser => chooser.Accounts.Select(account => account.ConfigUrl),
                IdentityProviderLoginPrompt login => [login.ConfigUrl],
                IdentityCredentialErrorPrompt error => [error.ConfigUrl],
                _ => [],
            };
            var providers = string.Join(", ", configUrls.Select(GetHost).Distinct(StringComparer.Ordinal));
            var title = prompt.RequestContext switch
            {
                IdentityCredentialRequestOptionsContext.Signup => $"Sign up to {topLevel} with {providers}",
                IdentityCredentialRequestOptionsContext.Use => $"Use {topLevel} with {providers}",
                IdentityCredentialRequestOptionsContext.Continue => $"Continue to {topLevel} with {providers}",
                _ => $"Sign in to {topLevel} with {providers}",
            };
            var site = GetHost(prompt.Origin);
            return (title, site != topLevel || prompt is IdentityAccountChooserPrompt { ShowsSiteOrigin: true } ? "on " + site : null);
        }

        private static string GetHost(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
    }
}
