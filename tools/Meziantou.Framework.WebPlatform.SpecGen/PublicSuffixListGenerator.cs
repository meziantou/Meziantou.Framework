using System.Globalization;
using System.Text;

namespace SpecGen;

/// <summary>
/// Generates the rules of the Public Suffix List (https://publicsuffix.org/list/), used by the URL Standard to obtain the
/// public suffix and the registrable domain of a host (https://url.spec.whatwg.org/#host-public-suffix). The rules of the
/// ICANN and private sections are both used. The labels are converted to their ASCII form (the hosts are ASCII once parsed).
/// The same file is the source of the list of Meziantou.Framework.Uri (tools/Meziantou.Framework.Uri.PublicSuffix.Generator).
/// </summary>
internal static class PublicSuffixListGenerator
{
    public static void Generate(GeneratorContext context)
    {
        var idn = new IdnMapping { AllowUnassigned = true, UseStd3AsciiRules = false };

        // The rules are grouped by kind, without their prefix ("*." for a wildcard rule, "!" for an exception rule), so the
        // runtime only splits each group
        var normal = new SortedSet<string>(StringComparer.Ordinal);
        var wildcards = new SortedSet<string>(StringComparer.Ordinal);
        var exceptions = new SortedSet<string>(StringComparer.Ordinal);
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
            var rule = string.Join('.', labels);
            if (exception)
            {
                exceptions.Add(rule);
            }
            else if (rule.StartsWith("*.", StringComparison.Ordinal))
            {
                wildcards.Add(rule[2..]);
            }
            else
            {
                normal.Add(rule);
            }
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
            WriteRules(writer, "NormalRules", "The normal rules.", normal);
            writer.Line();
            WriteRules(writer, "WildcardRules", "The suffixes of the wildcard rules (without the leading <c>*.</c>).", wildcards);
            writer.Line();
            WriteRules(writer, "ExceptionRules", "The exception rules (without the leading <c>!</c>).", exceptions);
        }

        context.WriteFile(context.Source("Urls", "PublicSuffixList.g.cs"), writer.ToString());
    }

    private static void WriteRules(CodeWriter writer, string name, string summary, IEnumerable<string> rules)
    {
        // A raw string literal: a concatenation of thousands of literals is a deeply nested expression, which overflows the
        // stack of the analyzers that walk the operation trees recursively (InsufficientExecutionStackException on Windows).
        // An array initializer would compile to a method with an instruction per rule, larger than the string itself.
        writer.Line("/// <summary>" + summary + " The rules are separated by a space or a line break.</summary>");
        writer.Line("private const string " + name + " = \"\"\"");
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
        foreach (var rulesLine in lines)
        {
            writer.Line("    " + rulesLine.TrimEnd());
        }

        writer.Line("    \"\"\";");
    }
}
