using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The dialogs of FedCM (the account chooser, the error dialog...)
internal sealed partial class WptUserAgentUI
{
    private static bool TryExecuteFedCmCommand(string command, JsonArray arguments, out string? result)
    {
        result = null;
        return false;
    }
}
