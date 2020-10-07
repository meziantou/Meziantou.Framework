﻿using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning
{
    public sealed class PythonRequirementsDependencyScanner : DependencyScanner
    {
        private static readonly Regex s_pypiReferenceRegex = new Regex(@"^(?<PACKAGENAME>[\w\.-]+?)\s?(\[.*\])?\s?==\s?(?<VERSION>[\w\.-]*?)$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(2));

        public override bool ShouldScanFile(CandidateFileContext file)
        {
            return file.FileName.Equals("requirements.txt", StringComparison.Ordinal);
        }

        public override async ValueTask ScanAsync(ScanFileContext context)
        {
            using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            var lineNo = 0;
            string? line;
            while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                lineNo++;

                var match = s_pypiReferenceRegex.Match(line);
                if (!match.Success)
                    continue;

                // Name==1.2.2
                var packageName = match.Groups["PACKAGENAME"].Value;
                var versionGroup = match.Groups["VERSION"];
                var version = versionGroup.Value;

                var column = versionGroup.Index + 1;
                await context.ReportDependency(new Dependency(packageName, version, DependencyType.PyPi, new TextLocation(context.FullPath, lineNo, column, versionGroup.Length))).ConfigureAwait(false);
            }
        }
    }
}
