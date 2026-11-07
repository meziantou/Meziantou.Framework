using System.Runtime.InteropServices;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>
/// A Docker image reference, <c>[registry[:port]/]repository[:tag][@digest]</c>, split into its parts.
/// Offsets are relative to the parsed text.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct DockerImageReference
{
    private DockerImageReference(string name, string? tag, int tagIndex, string? digest, bool hasPathSeparator)
    {
        Name = name;
        HasPathSeparator = hasPathSeparator;
        Tag = tag;
        TagIndex = tagIndex;
        Digest = digest;
    }

    public string Name { get; }

    public string? Tag { get; }

    public int TagIndex { get; }

    public string? Digest { get; }

    /// <summary>Gets a value indicating whether the name contains a <c>/</c> outside of variable references, such as <c>library/node</c> or <c>ghcr.io/owner/image</c>.</summary>
    public bool HasPathSeparator { get; }

    /// <summary>
    /// Gets a value indicating whether the reference is certainly an image rather than another kind of name, such as an alias or a build stage:
    /// it has a tag, a digest, or a registry or repository path. Variable references do not count.
    /// </summary>
    public bool IsQualified => Tag is not null || Digest is not null || HasPathSeparator;

    /// <summary>
    /// Parses an image reference. The tag separator is the last <c>:</c> after the last <c>/</c>, so a registry port
    /// (<c>localhost:5000/image</c>) is part of the name, and <c>@</c> introduces a digest. Variable references such as
    /// <c>${VERSION:-1}</c>, <c>$(Version)</c> or <c>${{ matrix.version }}</c> are kept opaque, so their content never
    /// splits the reference.
    /// </summary>
    public static bool TryParse(string value, out DockerImageReference reference)
    {
        reference = default;
        if (string.IsNullOrEmpty(value))
            return false;

        var lastSlash = -1;
        var lastColon = -1;
        var at = -1;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '$' && i + 1 < value.Length && value[i + 1] is '{' or '(' or '[')
            {
                i = SkipVariable(value, i + 1);
                continue;
            }

            if (char.IsWhiteSpace(c))
                return false;

            if (c is '@')
            {
                at = i;
                break;
            }

            if (c is '/')
            {
                lastSlash = i;
                lastColon = -1;
            }
            else if (c is ':')
            {
                lastColon = i;
            }
        }

        var nameEnd = at >= 0 ? at : value.Length;
        string? digest = null;
        if (at >= 0)
        {
            digest = value[(at + 1)..];
            if (digest.Length is 0)
                return false;
        }

        string? tag = null;
        var tagIndex = -1;
        if (lastColon > lastSlash)
        {
            tagIndex = lastColon + 1;
            tag = value[tagIndex..nameEnd];
            if (tag.Length is 0)
                return false;

            nameEnd = lastColon;
        }

        if (nameEnd is 0)
            return false;

        reference = new DockerImageReference(value[..nameEnd], tag, tagIndex, digest, lastSlash >= 0);
        return true;
    }

    /// <summary>
    /// Reports an image reference. <paramref name="getLocation"/> maps a (start, length) range of <paramref name="value"/> to a location.
    /// A reference pinned by digest is reported with the digest as a non-updatable version, since changing its tag would
    /// leave the digest pointing at the old image. Names and versions that contain a variable reference are not updatable.
    /// </summary>
    public static void Report(DependencyScanner scanner, ScanFileContext context, string value, Func<int, int, Location?> getLocation, bool requireTagOrDigest = false)
    {
        if (!TryParse(value, out var reference))
            return;

        if (requireTagOrDigest && reference.Tag is null && reference.Digest is null)
            return;

        var nameLocation = IsVariable(reference.Name) ? new NonUpdatableLocation(context) : getLocation(0, reference.Name.Length);
        if (reference.Digest is not null)
        {
            context.ReportDependency(
                scanner,
                reference.Name,
                reference.Digest,
                DependencyType.DockerImage,
                nameLocation,
                new NonUpdatableLocation(context),
                tags: [],
                metadata: [
                    KeyValuePair.Create<string, object?>("digest", reference.Digest),
                    KeyValuePair.Create<string, object?>("tag", reference.Tag),
                ]);
        }
        else if (reference.Tag is not null)
        {
            var versionLocation = IsVariable(reference.Tag) ? new NonUpdatableLocation(context) : getLocation(reference.TagIndex, reference.Tag.Length);
            context.ReportDependency(scanner, reference.Name, reference.Tag, DependencyType.DockerImage, nameLocation, versionLocation);
        }
        else
        {
            context.ReportDependency(scanner, reference.Name, version: null, DependencyType.DockerImage, nameLocation, versionLocation: null);
        }

        static bool IsVariable(string value) => value.Contains('$', StringComparison.Ordinal);
    }

    private static int SkipVariable(string value, int openIndex)
    {
        var open = value[openIndex];
        var close = open switch
        {
            '{' => '}',
            '(' => ')',
            _ => ']',
        };

        var depth = 0;
        for (var i = openIndex; i < value.Length; i++)
        {
            if (value[i] == open)
            {
                depth++;
            }
            else if (value[i] == close)
            {
                depth--;
                if (depth is 0)
                    return i;
            }
        }

        return value.Length;
    }
}
