using System.Globalization;
using System.Text;

namespace SpecGen;

/// <summary>
/// Generates the rules of the Public Suffix List (https://publicsuffix.org/list/), used by the URL Standard to obtain the
/// public suffix and the registrable domain of a host (https://url.spec.whatwg.org/#host-public-suffix). The rules of the
/// ICANN and private sections are both used. The labels are converted to their ASCII form (the hosts are ASCII once parsed).
/// </summary>
internal static class PublicSuffixListGenerator
{
    public static void Generate(GeneratorContext context)
    {
        var idn = new IdnMapping { AllowUnassigned = true, UseStd3AsciiRules = false };
        var rules = new SortedSet<string>(StringComparer.Ordinal);
        string? version = null;
        foreach (var rawLine in File.ReadLines(context.SpecData("publicsuffix", "public_suffix_list.dat")))
        {
            // https://github.com/publicsuffix/list/wiki/Format: one rule per line, up to the first whitespace; "//" starts a comment
            var line = rawLine.Trim();
            if (line.StartsWith("// VERSION: ", StringComparison.Ordinal))
            {
                version = line["// VERSION: ".Length..];
            }

            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var whitespace = line.IndexOfAny([' ', '\t']);
            if (whitespace >= 0)
            {
                line = line[..whitespace];
            }

            var exception = line.StartsWith('!', StringComparison.Ordinal);
            var labels = (exception ? line[1..] : line).Split('.').Select(label => label == "*" ? label : idn.GetAscii(label).ToLowerInvariant());
            rules.Add((exception ? "!" : "") + string.Join('.', labels));
        }

        // The rules are data of the list, licensed under the MPL 2.0 (its Exhibit A notice is kept, see THIRD-PARTY-NOTICES.md)
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/publicsuffix/public_suffix_list.dat");
        writer.Line("// The rules of this file come from the Public Suffix List (https://publicsuffix.org/list/public_suffix_list.dat)" + (version is null ? "" : ", version " + version) + ":");
        writer.Line("// This Source Code Form is subject to the terms of the Mozilla Public");
        writer.Line("// License, v. 2.0. If a copy of the MPL was not distributed with this");
        writer.Line("// file, You can obtain one at https://mozilla.org/MPL/2.0/.").Line();
        writer.Line("namespace Meziantou.Framework.WebPlatform.Urls;").Line();
        using (writer.Block("internal static partial class PublicSuffixList"))
        {
            writer.Line("/// <summary>The rules of the list, separated by a space: <c>*.</c> starts a wildcard rule, <c>!</c> an exception rule.</summary>");
            writer.Line("private const string RulesData =");
            var line = new StringBuilder();
            var lines = new List<string>();
            foreach (var rule in rules)
            {
                if (line.Length > 0 && line.Length + rule.Length > 110)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }

                line.Append(rule).Append(' ');
            }

            lines.Add(line.ToString().TrimEnd());
            for (var i = 0; i < lines.Count; i++)
            {
                writer.Line("    " + CodeWriter.Literal(lines[i]) + (i == lines.Count - 1 ? ";" : " +"));
            }
        }

        context.WriteFile(context.Source("Urls", "PublicSuffixList.g.cs"), writer.ToString());
    }
}
