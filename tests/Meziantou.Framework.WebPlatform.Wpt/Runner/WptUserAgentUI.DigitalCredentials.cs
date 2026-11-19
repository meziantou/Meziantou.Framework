using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The virtual wallet of the Digital Credentials API
internal sealed partial class WptUserAgentUI
{
    private static bool TryExecuteDigitalCredentialCommand(string command, JsonArray arguments, out string? result)
    {
        result = null;
        return false;
    }
}
