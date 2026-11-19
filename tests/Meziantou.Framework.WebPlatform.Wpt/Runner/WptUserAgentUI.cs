using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The user interface of the user agent of the runner (<see cref="BrowsingContextOptions.UserAgentUI"/>): a deterministic
/// implementation of the prompts, driven by the commands of the test driver that the tests use (the transaction mode of
/// Secure Payment Confirmation, the account chooser of FedCM, the virtual wallet of the Digital Credentials API...). Each API
/// has its own partial file, with its prompts and its commands (see <see cref="Execute"/>). The prompts are answered on the
/// event loop of the test; a prompt that waits for a command of the test driver (e.g. an account chooser) completes when the
/// command runs.
/// </summary>
internal sealed partial class WptUserAgentUI : UserAgentUIBackend
{
    /// <summary>
    /// Runs a command of the test driver (from a task of the event loop of the test): <paramref name="arguments"/> are the
    /// arguments of the testdriver function. Returns the JSON result (null for no result), or throws a
    /// <see cref="WptWebDriverException"/> for a WebDriver error.
    /// </summary>
    public string? Execute(string command, JsonArray arguments)
    {
        if (TryExecuteCredentialCommand(command, arguments, out var result)
            || TryExecutePaymentCommand(command, arguments, out result)
            || TryExecuteFedCmCommand(command, arguments, out result)
            || TryExecuteDigitalCredentialCommand(command, arguments, out result))
        {
            return result;
        }

        throw new WptWebDriverException($"unknown command: {command}");
    }

    internal static string GetString(JsonNode? value, string name)
        => value?.GetValueKind() == System.Text.Json.JsonValueKind.String ? value.GetValue<string>() : throw WptWebDriverException.InvalidArgument($"{name} is not a string");
}
