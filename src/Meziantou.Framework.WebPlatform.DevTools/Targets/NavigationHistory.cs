using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

/// <summary>The session history of a page (<c>Page.getNavigationHistory</c>): the entries of the top-level browsing context, identified by their step.</summary>
internal static class NavigationHistory
{
    public static JsonObject Get(BrowsingContext browsingContext)
    {
        var entries = GetEntries(browsingContext);
        var currentIndex = GetCurrentIndex(browsingContext, entries);
        var result = new JsonArray();
        foreach (var entry in entries)
        {
            string title;
            try
            {
                title = entry.Document?.Title ?? "";
            }
            catch (InvalidOperationException)
            {
                title = "";
            }

            var url = entry.Url.Serialize();
            result.Add((JsonNode)new JsonObject
            {
                ["id"] = entry.Step,
                ["url"] = url,
                ["userTypedURL"] = url,
                ["title"] = title,
                ["transitionType"] = "typed",
            });
        }

        return new JsonObject { ["currentIndex"] = currentIndex, ["entries"] = result };
    }

    /// <summary>Traverses the session history to the entry of a step (<c>Page.navigateToHistoryEntry</c>).</summary>
    public static void NavigateTo(BrowsingContext browsingContext, int entryId)
    {
        var entries = GetEntries(browsingContext);
        var targetIndex = entries.FindIndex(entry => entry.Step == entryId);
        if (targetIndex < 0)
            throw new ProtocolException("No entry with passed id");

        var steps = browsingContext.GetAllUsedHistorySteps();
        var currentPosition = steps.IndexOf(browsingContext.CurrentStep);
        var targetPosition = steps.IndexOf(entryId);
        if (currentPosition < 0 || targetPosition < 0 || currentPosition == targetPosition)
            return;

        browsingContext.TraverseByDelta(targetPosition - currentPosition);
    }

    private static List<SessionHistoryEntry> GetEntries(BrowsingContext browsingContext)
        => [.. (browsingContext.GetSessionHistoryEntries() ?? []).Where(entry => entry.Step >= 0).OrderBy(entry => entry.Step)];

    private static int GetCurrentIndex(BrowsingContext browsingContext, List<SessionHistoryEntry> entries)
    {
        var current = browsingContext.CurrentStep;
        var index = -1;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Step <= current)
            {
                index = i;
            }
        }

        return Math.Max(0, index);
    }
}
