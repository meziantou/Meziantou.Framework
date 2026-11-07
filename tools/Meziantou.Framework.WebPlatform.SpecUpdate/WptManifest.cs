using System.IO.Compression;
using System.Text.Json;

namespace SpecUpdate;

/// <summary>Reads the manifest of the web-platform-tests (<c>MANIFEST.json.gz</c>, https://web-platform-tests.org/tools/manifest/).</summary>
internal static class WptManifest
{
    /// <summary>
    /// Returns the files of the items of a type of the manifest (e.g. <c>testharness</c>, <c>reftest</c>) and their variants:
    /// <c>[url or null, ...]</c> (the reftests have <c>[url or null, references, extras]</c>).
    /// </summary>
    public static Dictionary<string, List<JsonElement>> ReadItems(JsonDocument manifest, string type)
    {
        var result = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        if (manifest.RootElement.GetProperty("items").TryGetProperty(type, out var items))
        {
            Walk(items, "");
        }

        return result;

        // The items are a tree of directories whose leaves are the test files: [hash, variant, variant...]
        void Walk(JsonElement directory, string prefix)
        {
            foreach (var property in directory.EnumerateObject())
            {
                var path = prefix + property.Name;
                if (property.Value.ValueKind is JsonValueKind.Object)
                {
                    Walk(property.Value, path + "/");
                }
                else
                {
                    result[path] = [.. property.Value.EnumerateArray().Skip(1)];
                }
            }
        }
    }

    public static JsonDocument Open(string manifestPath)
    {
        using var stream = new GZipStream(File.OpenRead(manifestPath), CompressionMode.Decompress);
        return JsonDocument.Parse(stream);
    }

    /// <summary>
    /// Returns the files of the references of the reftests of the paths of a sparse checkout (and of their references) that the
    /// paths do not contain, so that they can be fetched with the tests (e.g. <c>css/reference/ref-filled-green-100px-square.xht</c>).
    /// </summary>
    public static IEnumerable<string> GetReferencesOutsideOfPaths(string manifestPath, string[] paths)
    {
        using var manifest = Open(manifestPath);
        var reftests = ReadItems(manifest, "reftest");

        // (and the ones of the print reftests)
        foreach (var (path, variants) in ReadItems(manifest, "print-reftest"))
        {
            reftests[path] = variants;
        }

        // The references of each page (a reference can be a reftest itself), by path without the query
        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (path, variants) in reftests)
        {
            foreach (var variant in variants)
            {
                var url = variant[0].ValueKind is JsonValueKind.String ? variant[0].GetString()!.TrimStart('/') : path;
                var key = StripQuery(url);
                if (!references.TryGetValue(key, out var set))
                {
                    references[key] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                foreach (var reference in variant[1].EnumerateArray())
                {
                    set.Add(StripQuery(reference[0].GetString()!.TrimStart('/')));
                }
            }
        }

        var result = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(reftests.Keys.Where(path => IsInPaths(path, paths)));
        var visited = new HashSet<string>(queue, StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            if (!references.TryGetValue(queue.Dequeue(), out var pageReferences))
                continue;

            foreach (var reference in pageReferences)
            {
                if (!visited.Add(reference))
                    continue;

                queue.Enqueue(reference);

                // A reference can be another URL than a file of the test suite (about:blank)
                if (!reference.Contains(':', StringComparison.Ordinal) && !IsInPaths(reference, paths))
                {
                    result.Add(reference);
                }
            }
        }

        return result;

        static string StripQuery(string url) => url.Split('?', '#')[0];
    }

    /// <summary>Returns whether a file is contained by a path of a sparse checkout: a directory or a file, whose '*' segments match any directory.</summary>
    public static bool IsInPaths(string file, string[] paths)
    {
        var fileSegments = file.Split('/');
        foreach (var path in paths)
        {
            var segments = path.Trim('/').Split('/');
            if (segments.Length > fileSegments.Length)
                continue;

            var match = true;
            for (var i = 0; i < segments.Length && match; i++)
            {
                match = segments[i] == "*" || segments[i] == fileSegments[i];
            }

            if (match)
                return true;
        }

        return false;
    }
}
