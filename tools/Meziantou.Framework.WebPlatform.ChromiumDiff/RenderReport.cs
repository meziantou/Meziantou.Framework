using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ChromiumDiff;

/// <summary>Writes the reports of the rendering comparison: report.html (with the images), report.json, and the summary in Markdown.</summary>
internal static class RenderReport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };

    public static void Write(string directory, string chromiumVersion, string rasterizer, IReadOnlyList<RenderScenario> scenarios, IReadOnlyList<RenderResult> results, IReadOnlyList<RenderKnownDifference> stale)
    {
        File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(results.Select(r => new
        {
            r.Scenario,
            r.Fixture,
            r.Url,
            r.Output,
            Status = r.Status.ToString(),
            r.Metrics,
            r.Unsupported,
            r.Message,
            r.Images,
        }), JsonOptions));

        File.WriteAllText(Path.Combine(directory, "report.md"), CreateSummary(chromiumVersion, rasterizer, scenarios, results));

        var html = new StringBuilder();
        html.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <meta charset="utf-8">
            <title>Rendering comparison</title>
            <style>
              body { font: 14px system-ui, sans-serif; margin: 16px; background: #fafafa; color: #222 }
              table { border-collapse: collapse; margin-bottom: 24px }
              td, th { border: 1px solid #ccc; padding: 4px 8px; vertical-align: top; text-align: left }
              .Pass { background: #dfd } .Known { background: #eef } .Unsupported { background: #ffd } .Unexpected { background: #fdd } .Error { background: #f99 }
              img { max-width: 260px; max-height: 260px; border: 1px solid #999; background: repeating-conic-gradient(#ccc 0 25%, #fff 0 50%) 0 0 / 16px 16px }
              .filters label { margin-right: 12px }
              .hidden { display: none }
            </style>
            <h1>Rendering comparison: Playwright (Chromium) vs Meziantou.Framework.WebPlatform</h1>
            """);
        html.Append(CultureInfo.InvariantCulture, $"<p>Chromium {Encode(chromiumVersion)}, PDF rasterizer {Encode(rasterizer)}. The images are chromium / library / diff (the different pixels in red). Click an image to open it.</p>");
        html.Append("<p class=filters>");
        foreach (var status in Enum.GetValues<RenderStatus>())
        {
            var count = results.Count(r => r.Status == status);
            html.Append(CultureInfo.InvariantCulture, $"<label><input type=checkbox checked data-status=\"{status}\"> {status} ({count})</label>");
        }

        html.Append("</p>");
        if (stale.Count > 0)
        {
            html.Append("<p><b>Stale known differences:</b></p><ul>");
            foreach (var known in stale)
                html.Append(CultureInfo.InvariantCulture, $"<li>{Encode(known.Fixture)} {Encode(known.Scenario)}: {Encode(known.Reason)}</li>");

            html.Append("</ul>");
        }

        var ranked = RankPages(results);
        if (ranked.Count > 0)
        {
            html.Append("<h2 id=\"pages\">Real-world pages, by difference</h2><p>The pages of pages.txt, the most different first (the errors, then by the fraction of the pixels that differ), with the most different scenario of each page.</p>");
            html.Append("<table><tr><th>#</th><th>Page</th><th>Scenario</th><th>Status</th><th>Metrics</th></tr>");
            for (var i = 0; i < ranked.Count; i++)
            {
                var result = ranked[i];
                var anchor = result.Scenario + "-" + Path.GetFileNameWithoutExtension(result.Fixture);
                html.Append(CultureInfo.InvariantCulture, $"<tr class=\"{result.Status}\" data-status=\"{result.Status}\"><td>{i + 1}</td><td><a href=\"#{Encode(anchor)}\">{Encode(result.Url!)}</a><br>{Encode(result.Fixture)}</td><td>{Encode(result.Scenario)}</td><td>{result.Status}</td><td>{Encode(result.Metrics?.Describe() ?? result.Message ?? "")}</td></tr>");
            }

            html.Append("</table>");
        }

        foreach (var scenario in scenarios)
        {
            var scenarioResults = results.Where(r => r.Scenario == scenario.Name).ToList();
            if (scenarioResults.Count == 0)
                continue;

            html.Append(CultureInfo.InvariantCulture, $"<h2 id=\"{Encode(scenario.Name)}\">{Encode(scenario.Name)}</h2><p>{Encode(scenario.Description)}</p>");
            html.Append("<table><tr><th>Fixture</th><th>Output</th><th>Status</th><th>Metrics</th><th>Images</th></tr>");
            foreach (var result in scenarioResults)
            {
                var id = result.Url is null ? "" : $" id=\"{Encode(result.Scenario + "-" + Path.GetFileNameWithoutExtension(result.Fixture))}\"";
                var fixture = result.Url is null ? Encode(result.Fixture) : $"{Encode(result.Fixture)}<br><a href=\"{Encode(result.Url)}\">{Encode(result.Url)}</a>";
                html.Append(CultureInfo.InvariantCulture, $"<tr{id} class=\"{result.Status}\" data-status=\"{result.Status}\"><td>{fixture}</td><td>{Encode(result.Output)}</td><td>{result.Status}");
                if (result.Unsupported.Count > 0)
                    html.Append(CultureInfo.InvariantCulture, $"<br>unsupported: {Encode(string.Join(", ", result.Unsupported))}");

                if (result.Message is not null)
                    html.Append(CultureInfo.InvariantCulture, $"<br>{Encode(result.Message)}");

                html.Append(CultureInfo.InvariantCulture, $"</td><td>{Encode(result.Metrics?.Describe() ?? "")}</td><td>");
                foreach (var image in result.Images)
                {
                    var prefix = image.Replace('\\', '/');
                    var separator = result.Output == "screenshot" ? "/" : "-";
                    foreach (var kind in (ReadOnlySpan<string>)["chromium", "library", "diff"])
                    {
                        var path = prefix + separator + kind + ".png";
                        html.Append(CultureInfo.InvariantCulture, $"<a href=\"{Encode(path)}\"><img loading=lazy src=\"{Encode(path)}\" alt=\"{kind}\" title=\"{kind}\"></a> ");
                    }

                    html.Append("<br>");
                }

                html.Append("</td></tr>");
            }

            html.Append("</table>");
        }

        html.Append("""
            <script>
              for (const input of document.querySelectorAll('input[data-status]')) {
                input.addEventListener('change', () => {
                  for (const row of document.querySelectorAll(`tr[data-status="${input.dataset.status}"]`))
                    row.classList.toggle('hidden', !input.checked);
                });
              }
            </script>
            """);
        File.WriteAllText(Path.Combine(directory, "report.html"), html.ToString());
    }

    /// <summary>Writes the summary that is committed (render-parity.md): no image and no timing, so that it changes only with the results.</summary>
    public static void WriteSummary(string path, string chromiumVersion, string rasterizer, IReadOnlyList<RenderScenario> scenarios, IReadOnlyList<RenderResult> results)
    {
        File.WriteAllText(path, CreateSummary(chromiumVersion, rasterizer, scenarios, results));
    }

    private static string CreateSummary(string chromiumVersion, string rasterizer, IReadOnlyList<RenderScenario> scenarios, IReadOnlyList<RenderResult> results)
    {
        var md = new StringBuilder();
        md.Append("# Export parity with Playwright (Chromium)\n\n");
        md.Append("<!-- Generated by `dotnet run --project tools/Meziantou.Framework.WebPlatform.ChromiumDiff -- --render --summary tools/Meziantou.Framework.WebPlatform.ChromiumDiff/render-parity.md`: do not edit -->\n\n");
        md.Append(CultureInfo.InvariantCulture, $"The fixtures of `render-corpus` exported by Playwright (Chromium {chromiumVersion}) and by the library, compared pixel by pixel (the PDF are rasterized by {rasterizer}). ");
        md.Append("A result is *unsupported* when the library has no equivalent of a Playwright option of the scenario (the library renders without it), *known* when the difference is accepted in `render-known-differences.json`.\n\n");

        md.Append("| Status | Count |\n|---|---|\n");
        foreach (var status in Enum.GetValues<RenderStatus>())
            md.Append(CultureInfo.InvariantCulture, $"| {status} | {results.Count(r => r.Status == status)} |\n");

        md.Append("\n## Scenarios\n\n| Scenario | Playwright options | Unsupported options | Pass | Known | Unsupported | Unexpected | Error |\n|---|---|---|---|---|---|---|---|\n");
        foreach (var scenario in scenarios)
        {
            var scenarioResults = results.Where(r => r.Scenario == scenario.Name).ToList();
            if (scenarioResults.Count == 0)
                continue;

            var unsupported = string.Join(", ", scenarioResults.SelectMany(r => r.Unsupported).Distinct(StringComparer.Ordinal));
            md.Append(CultureInfo.InvariantCulture, $"| {scenario.Name} | {Escape(scenario.Description)} | {Escape(unsupported)} | {Count(RenderStatus.Pass)} | {Count(RenderStatus.Known)} | {Count(RenderStatus.Unsupported)} | {Count(RenderStatus.Unexpected)} | {Count(RenderStatus.Error)} |\n");

            int Count(RenderStatus status) => scenarioResults.Count(r => r.Status == status);
        }

        md.Append("\n## Results\n\n| Scenario | Fixture | Output | Status | Metrics | Notes |\n|---|---|---|---|---|---|\n");
        foreach (var scenario in scenarios)
        {
            foreach (var result in results.Where(r => r.Scenario == scenario.Name))
            {
                var notes = string.Join("; ", (result.Unsupported.Count > 0 ? ["unsupported: " + string.Join(", ", result.Unsupported)] : Array.Empty<string>()).Concat(result.Message is null ? [] : [result.Message]));
                md.Append(CultureInfo.InvariantCulture, $"| {result.Scenario} | {result.Fixture} | {result.Output} | {result.Status} | {Escape(result.Metrics?.Describe() ?? "")} | {Escape(notes)} |\n");
            }
        }

        var ranked = RankPages(results);
        if (ranked.Count > 0)
        {
            md.Append("\n## Real-world pages, by difference\n\nThe pages of pages.txt (`--pages`), the most different first: the errors, then by the fraction of the pixels that differ (the most different scenario of each page).\n\n| # | Page | Scenario | Status | Metrics |\n|---|---|---|---|---|\n");
            for (var i = 0; i < ranked.Count; i++)
            {
                var result = ranked[i];
                md.Append(CultureInfo.InvariantCulture, $"| {i + 1} | {Escape(result.Url!)} ({result.Fixture}) | {result.Scenario} | {result.Status} | {Escape(result.Metrics?.Describe() ?? result.Message ?? "")} |\n");
            }
        }

        return md.ToString();
    }

    /// <summary>
    /// Ranks the pages of pages.txt to find the largest differences: the most different result of each page (its scenarios
    /// differ little), the errors first, then by the fraction of the pixels that differ, then by SSIM.
    /// </summary>
    private static List<RenderResult> RankPages(IReadOnlyList<RenderResult> results)
        => [.. Rank(results.Where(r => r.Url is not null).GroupBy(r => r.Url, StringComparer.Ordinal).Select(g => Rank(g).First()))];

    private static IOrderedEnumerable<RenderResult> Rank(IEnumerable<RenderResult> results)
        => results
            .OrderByDescending(r => r.Status is RenderStatus.Error)
            .ThenByDescending(r => r.Metrics?.DifferentPixels ?? 0)
            .ThenBy(r => r.Metrics?.Ssim ?? 0)
            .ThenBy(r => r.Url, StringComparer.Ordinal)
            .ThenBy(r => r.Scenario, StringComparer.Ordinal);

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
