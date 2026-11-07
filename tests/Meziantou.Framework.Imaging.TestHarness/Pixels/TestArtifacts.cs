namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>
/// Location of diagnostic artifacts (expected/actual/difference previews and numeric reports) written when a golden
/// comparison fails. Previews help diagnosis; they are never the equality oracle.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_TEST_ARTIFACTS</c>: root directory (CI uploads it); defaults to <c>&lt;test output&gt;/TestArtifacts</c>.</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_TEST_PREVIEWS=false</c>: disables previews.</description></item>
/// </list>
/// </remarks>
public static class TestArtifacts
{
    public const string DirectoryVariable = "MEZIANTOU_FRAMEWORK_IMAGING_TEST_ARTIFACTS";
    public const string PreviewsVariable = "MEZIANTOU_FRAMEWORK_IMAGING_TEST_PREVIEWS";

    /// <summary>Gets a value indicating whether failure previews are written.</summary>
    public static bool PreviewsEnabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(PreviewsVariable);
            return !(string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || value == "0");
        }
    }

    /// <summary>Gets the root artifacts directory (not created).</summary>
    public static FullPath RootDirectory
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(DirectoryVariable);
            return string.IsNullOrWhiteSpace(value) ? FullPath.FromPath(AppContext.BaseDirectory) / "TestArtifacts" : FullPath.FromPath(value);
        }
    }

    /// <summary>Gets (and creates) a directory dedicated to a test or fixture.</summary>
    /// <param name="name">A name such as a fixture id; characters invalid in file names and '/' are replaced.</param>
    /// <returns>The directory path.</returns>
    public static FullPath GetDirectory(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var path = RootDirectory / SanitizeFileName(name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Makes a string usable as a file name on every platform.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The sanitized name.</returns>
    public static string SanitizeFileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(chars[i]) || chars[i] is '-' or '_' or '.'))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
