---
title: Custom scanners
description: Read your own file formats with a regular expression, or write a scanner that parses them.
---

## Use a regular expression

[RegexScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.RegexScanner) reads the files that match its patterns with a regular expression. It is enough for formats where a
dependency is written on one line:

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanner_CustomRegexScanner;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ScannerOptions
{
    Scanners =
    [
        new RegexScanner
        {
            FilePatterns = [Glob.Parse("**/*.custom", GlobDialect.Standard, GlobOptions.IgnoreCase)],
            DependencyType = DependencyType.DockerImage,
            Regex = new Regex(@"image:\s*(?<name>[a-z/]+)(:(?<version>[0-9.]+))?", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)),
        },
    ],
};

var dependencies = await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options,
    cancellationToken);
```
<!-- endref -->

- [FilePatterns](cref:Meziantou.Framework.DependencyScanning.Scanners.RegexScanner.FilePatterns) are globs, from `Meziantou.Framework.Globbing`, matched against the path of the file relative to the
  scanned root folder, for instance `build/*.yml` or `**/*.custom`. See [Globbing](../Globbing/01-Introduction.md).
- The regular expression must have a `name` group, and can have a `version` group. Each match is a dependency of
  [DependencyType](cref:Meziantou.Framework.DependencyScanning.DependencyType).
- The name and the version are updatable.
- Setting [Scanners](cref:Meziantou.Framework.DependencyScanning.ScannerOptions.Scanners) replaces the built-in scanners. Add the regex scanner to them to keep them, as in the next example.

## Write a scanner

For other formats, derive from [DependencyScanner](cref:Meziantou.Framework.DependencyScanning.DependencyScanner):

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanning_CustomScanner;format=md-fenced-code-block;dedent=true -->
```csharp
// Reads the "name version" lines of the versions.txt files
public sealed class VersionsFileScanner : DependencyScanner
{
    protected override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Unknown];

    // Called for every file: keep it cheap, the content is only read for the files it accepts
    protected override bool ShouldScanFileCore(CandidateFileContext context)
        => context.HasFileName("versions.txt", ignoreCase: true);

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var reader = new StreamReader(context.Content, leaveOpen: true);
        while (await reader.ReadLineAsync(context.CancellationToken) is { } line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 2)
            {
                // Without a location, the dependency is reported but cannot be updated
                context.ReportDependency(this, parts[0], parts[1], DependencyType.Unknown, nameLocation: null, versionLocation: null);
            }
        }
    }
}
```
<!-- endref -->

- [ShouldScanFileCore](cref:Meziantou.Framework.DependencyScanning.DependencyScanner.ShouldScanFileCore) decides from the path whether the scanner reads a file. It runs for every file of the scan, so
  keep it cheap: [CandidateFileContext](cref:Meziantou.Framework.DependencyScanning.CandidateFileContext) has the [FileName](cref:Meziantou.Framework.DependencyScanning.CandidateFileContext.FileName), the [Directory](cref:Meziantou.Framework.DependencyScanning.CandidateFileContext.Directory), the [RelativeDirectory](cref:Meziantou.Framework.DependencyScanning.CandidateFileContext.RelativeDirectory) and the
  [RootDirectory](cref:Meziantou.Framework.DependencyScanning.CandidateFileContext.RootDirectory) of the file as spans, and the [HasFileName](cref:Meziantou.Framework.DependencyScanning.CandidateFileContext.HasFileName) and [HasExtension](cref:Meziantou.Framework.DependencyScanning.CandidateFileContext.HasExtension) helpers.
- [SupportedDependencyTypes](cref:Meziantou.Framework.DependencyScanning.DependencyScanner.SupportedDependencyTypes) lists the types the scanner reports. It decides whether the scanner runs when the options
  filter the dependency types, and reporting another type throws an `InvalidOperationException`.
- [ScanAsync](cref:Meziantou.Framework.DependencyScanning.DependencyScanner.ScanAsync) reads [ScanFileContext.Content](cref:Meziantou.Framework.DependencyScanning.ScanFileContext.Content), a seekable stream, and calls [ReportDependency](cref:Meziantou.Framework.DependencyScanning.ScanFileContext.ReportDependency) for each dependency. Leave
  the stream open: the other scanners of the file read it too. An overload of [ReportDependency](cref:Meziantou.Framework.DependencyScanning.ScanFileContext.ReportDependency) adds tags and metadata.
- Scanners run in parallel on different files: keep them free of shared mutable state.

Add the scanner to the options:

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanning_CustomScannerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ScannerOptions();

// Keep the built-in scanners, and add the custom one
options.Scanners = options.Scanners.Add(new VersionsFileScanner());

var dependencies = await DependencyScanner.ScanDirectoryAsync("C:\\MyProject", options, cancellationToken);
```
<!-- endref -->

## Make the values updatable

A dependency reported without locations is listed, but cannot be updated. To make it updatable, derive from [Location](cref:Meziantou.Framework.DependencyScanning.Location):
[IsUpdatable](cref:Meziantou.Framework.DependencyScanning.Location.IsUpdatable) says whether the value can be written, and [UpdateCoreAsync](cref:Meziantou.Framework.DependencyScanning.Location.UpdateCoreAsync) receives the expected value and the new one.
Read the file through the [FileSystem](cref:Meziantou.Framework.DependencyScanning.Location.FileSystem) of the location, check that it holds the expected value, and throw a
[DependencyScannerException](cref:Meziantou.Framework.DependencyScanning.DependencyScannerException) when it does not, as the built-in locations do. See
[An update never writes blindly](06-UpdatingDependencies.md#an-update-never-writes-blindly).
