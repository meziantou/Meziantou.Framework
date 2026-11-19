using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The payment sheet of the Payment Request API and the transaction dialog of Secure Payment Confirmation
internal sealed partial class WptUserAgentUI
{
    private static bool TryExecutePaymentCommand(string command, JsonArray arguments, out string? result)
    {
        result = null;
        return false;
    }
}
