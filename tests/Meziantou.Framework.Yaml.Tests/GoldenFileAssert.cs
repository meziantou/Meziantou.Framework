using System.Reflection;

namespace Meziantou.Framework.Yaml.Tests;

internal static class GoldenFileAssert
{
    public static void AreEqual(string resourcePath, string actual)
    {
        var expected = ReadEmbeddedResource(resourcePath);
        var normalizedExpected = Normalize(expected);
        var normalizedActual = Normalize(actual);
        if (string.Equals(normalizedExpected, normalizedActual, StringComparison.Ordinal))
        {
            return;
        }

        var firstDifference = FindFirstDifference(normalizedExpected, normalizedActual);
        var expectedContext = Slice(normalizedExpected, firstDifference);
        var actualContext = Slice(normalizedActual, firstDifference);
        Assert.Fail(
            "Golden YAML mismatch.\n" +
            $"Resource: {resourcePath}\n" +
            $"First difference index: {firstDifference.ToString(CultureInfo.InvariantCulture)}\n" +
            $"Expected context: \"{expectedContext}\"\n" +
            $"Actual context:   \"{actualContext}\"");
    }

    private static string ReadEmbeddedResource(string resourcePath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var normalizedPath = resourcePath.Replace('\\', '.').Replace('/', '.');
        var resourceName = $"Meziantou.Framework.Yaml.Tests.files.{normalizedPath}";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            Assert.Fail($"Unable to find embedded resource '{resourceName}'.");
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int FindFirstDifference(string expected, string actual)
    {
        var minLength = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (expected[i] != actual[i])
            {
                return i;
            }
        }

        return minLength;
    }

    private static string Slice(string text, int center)
    {
        const int Window = 20;
        var start = Math.Max(0, center - Window);
        var length = Math.Min(text.Length - start, Window * 2);
        return text.Substring(start, length).Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
