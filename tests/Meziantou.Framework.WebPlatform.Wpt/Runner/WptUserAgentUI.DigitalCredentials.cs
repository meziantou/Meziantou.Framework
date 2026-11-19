using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The virtual wallet of the Digital Credentials API (https://w3c-fedid.github.io/digital-credentials/#automation), set by
// test_driver.set_virtual_wallet_behavior(): it declines the requests, answers them, or waits (the chooser stays open until
// the request is aborted). Without a behavior, the chooser stays open, as in a browser whose user does not answer.
internal sealed partial class WptUserAgentUI
{
    private VirtualWalletBehavior? _walletBehavior;

    public override ValueTask<DigitalCredentialResponse?> RequestDigitalCredentialAsync(DigitalCredentialPrompt prompt, CancellationToken cancellationToken)
    {
        return _walletBehavior switch
        {
            { Action: "decline" } => new((DigitalCredentialResponse?)null),
            { Action: "respond", Protocol: { } protocol, Response: { } response } => new(new DigitalCredentialResponse(protocol, response)),
            _ => new(WaitForCancellation(cancellationToken)),
        };

        static async Task<DigitalCredentialResponse?> WaitForCancellation(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            return null;
        }
    }

    /// <summary>https://w3c-fedid.github.io/digital-credentials/#set-virtual-wallet-behavior</summary>
    private bool TryExecuteDigitalCredentialCommand(string command, JsonArray arguments, out string? result)
    {
        result = null;
        if (command != "set_virtual_wallet_behavior")
            return false;

        var action = GetString(arguments[0], "action");
        var protocol = arguments.Count > 1 ? arguments[1] : null;
        var response = arguments.Count > 2 ? arguments[2] : null;
        switch (action)
        {
            case "respond":
                if (protocol is null || response is null)
                    throw WptWebDriverException.InvalidArgument("the respond action needs a protocol and a response");

                var protocolName = GetString(protocol, "protocol");
                if (!DigitalCredential.UserAgentAllowsProtocol(protocolName))
                    throw WptWebDriverException.InvalidArgument($"'{protocolName}' is not a digital credential protocol");

                _walletBehavior = new VirtualWalletBehavior(action, protocolName, response.ToJsonString());
                return true;

            case "decline" or "wait":
                if (protocol is not null || response is not null)
                    throw WptWebDriverException.InvalidArgument($"the {action} action has no protocol nor response");

                _walletBehavior = new VirtualWalletBehavior(action, Protocol: null, Response: null);
                return true;

            case "clear":
                _walletBehavior = null;
                return true;

            default:
                throw WptWebDriverException.InvalidArgument($"'{action}' is not a virtual wallet action");
        }
    }

    private sealed record VirtualWalletBehavior(string Action, string? Protocol, string? Response);
}
