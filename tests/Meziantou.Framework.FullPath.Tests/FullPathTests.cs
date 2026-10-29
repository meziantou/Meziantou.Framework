using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public sealed partial class FullPathTests
{
    [Fact]
    public void ReadmeExample_Usage()
    {
        #region FullPath_Usage
        // Create FullPath
        FullPath rootPath = FullPath.FromPath("demo"); // It automatically calls Path.GetFullPath to resolve the path
        FullPath filePath = FullPath.Combine(rootPath, "temp", "meziantou.txt"); // Use Path.Combine to join paths (you can combine as many path as you needed)
        FullPath temp = FullPath.GetTempPath(); // equivalent of Path.GetTempPath()
        FullPath cwd = FullPath.CurrentDirectory(); // equivalent of Environment.CurrentDirectory
        Assert.Equal(Path.GetFullPath("demo"), rootPath.Value);
        Assert.Equal(FullPath.FromPath(Path.GetTempPath()), temp);
        Assert.Equal(FullPath.FromPath(Environment.CurrentDirectory), cwd);

        // Combine path: you can use the / operator to join path
        FullPath filePath1 = rootPath / "temp" / "meziantou.txt";
        Assert.Equal(filePath, filePath1);

        // Or use the + operator to append a suffix to the current path
        FullPath backupFilePath = filePath1 + ".bak";
        Assert.Equal("meziantou.txt.bak", backupFilePath.Name);

        // Compare path
        // Comparisons are case-insensitive on Windows, macOS, iOS and tvOS, and case-sensitive elsewhere by default
        Assert.False(filePath == rootPath);
        Assert.False(filePath.Equals(rootPath, ignoreCase: false));

        // Get parent directory
        FullPath parent = filePath.Parent;
        Assert.Equal(rootPath / "temp", parent);

        // Get file/directory name - extension
        Assert.Equal("meziantou.txt", filePath.Name);
        Assert.Equal(".txt", filePath.Extension);
        Assert.Equal("meziantou.log", filePath.WithExtension(".log").Name);
        Assert.Equal("archive.zip", FullPath.FromPath("archive.tar.gz").WithExtension(".zip", replaceAllTrailingExtensions: true).Name);
        Assert.Equal("archive.zip", FullPath.FromPath("archive.tar.gz").WithExtension(".zip", extensionCount: 2).Name);
        Assert.Equal("other.txt", filePath.WithName("other.txt").Name);
        Assert.Equal("other.txt", filePath.WithNameWithoutExtension("other").Name);

        // Make relative path
        string relativePath = filePath.MakePathRelativeTo(rootPath);
        Assert.Equal(Path.Combine("temp", "meziantou.txt"), relativePath); // temp/meziantou.txt, or temp\meziantou.txt on Windows

        // Check if a path is under another path
        Assert.True(filePath.IsChildOf(rootPath));

        // Resolve to canonical final path (follows symbolic links/reparse points)
        Assert.True(temp.TryGetCanonicalPath(out var canonicalPath));
        Assert.NotNull(canonicalPath);

        // FullPath is implicitly converted to string, so it works well with File/Directory methods
        Assert.False(File.Exists(filePath));
        #endregion
    }

    [Fact]
    public void FullPathExample()
    {
        #region FullPath_Example
        // Create FullPath
        FullPath rootPath = FullPath.FromPath("demo");
        FullPath filePath = rootPath / "temp" / "file.txt";

        // Compare paths (case-insensitive on Windows, macOS, iOS and tvOS, case-sensitive elsewhere)
        Assert.False(filePath == rootPath);
        Assert.True(filePath.Parent.Parent == rootPath);

        // Get relative path (temp\file.txt on Windows, temp/file.txt elsewhere)
        string relativePath = filePath.MakePathRelativeTo(rootPath);
        Assert.Equal(Path.Combine("temp", "file.txt"), relativePath);
        #endregion
    }

    [Fact]
    public void ReadmeExample_JsonSourceGenerator()
    {
        var configuration = new Configuration(FullPath.FromPath("output"));
        var json = JsonSerializer.Serialize(configuration, AppJsonContext.Default.Configuration);

        Assert.Equal(configuration, JsonSerializer.Deserialize(json, AppJsonContext.Default.Configuration));
    }

    #region FullPath_JsonSourceGenerator
    [JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
    [JsonSerializable(typeof(Configuration))]
    internal sealed partial class AppJsonContext : JsonSerializerContext;

    internal sealed record Configuration(FullPath OutputPath);
    #endregion

    [Fact]
    public void Documentation_GettingStarted()
    {
        #region FullPath_GettingStarted
        FullPath root = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory());

        // Combine paths with the / operator
        FullPath settings = root / "config" / "settings.json";
        Assert.Equal("settings.json", settings.Name);
        Assert.Equal(root / "config", settings.Parent);

        // FullPath converts to string implicitly, so it works with every API that takes a path
        settings.CreateParentDirectory();
        File.WriteAllText(settings, "{}");
        Assert.True(File.Exists(settings));

        // Show the path relative to the root folder (config/settings.json, or config\settings.json on Windows)
        Assert.Equal(Path.Combine("config", "settings.json"), settings.MakePathRelativeTo(root));
        #endregion

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Documentation_Normalization()
    {
        #region FullPath_Normalization
        // A relative path is resolved against the current directory
        FullPath data = FullPath.FromPath("data");
        Assert.Equal(FullPath.CurrentDirectory() / "data", data);

        // "." and ".." segments are resolved, and the trailing separator is removed
        FullPath logs = FullPath.FromPath("data/./../logs/");
        Assert.Equal(FullPath.CurrentDirectory() / "logs", logs);
        Assert.Equal("logs", logs.Name);
        #endregion
    }

    [Fact]
    public void Documentation_Combine()
    {
        #region FullPath_Combine
        FullPath root = FullPath.GetTempPath() / "app";

        // The / operator and Combine are equivalent
        Assert.Equal(FullPath.Combine(root, "logs", "today.log"), root / "logs" / "today.log");

        // The right operand can hold several segments and ".."
        Assert.Equal(root / "data" / "input.csv", root / "logs/../data/input.csv");

        // The + operator appends text to the last segment
        Assert.Equal(root / "logs.old", root / "logs" + ".old");

        // Like Path.Combine, a rooted right operand replaces the left one
        string absolutePath = Environment.CurrentDirectory;
        Assert.Equal(FullPath.FromPath(absolutePath), root / absolutePath);
        #endregion
    }

    [Fact]
    public void Documentation_Empty()
    {
        #region FullPath_Empty
        // An empty string gives the empty path, not the current directory
        FullPath path = FullPath.FromPath("");
        Assert.True(path.IsEmpty);
        Assert.Equal(FullPath.Empty, path);
        Assert.Equal(default(FullPath), path);
        Assert.Equal("", path.Value);

        // A null FileSystemInfo gives the empty path too
        Assert.True(FullPath.FromFileSystemInfo(null).IsEmpty);
        #endregion
    }

    [Fact]
    public void Documentation_WellKnownFolders()
    {
        #region FullPath_WellKnownFolders
        FullPath temp = FullPath.GetTempPath();
        FullPath current = FullPath.CurrentDirectory();
        FullPath home = FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile);
        FullPath fromInfo = FullPath.FromFileSystemInfo(new DirectoryInfo("output"));

        Assert.False(temp.IsEmpty);
        Assert.False(current.IsEmpty);
        Assert.False(home.IsEmpty);
        Assert.Equal(current / "output", fromInfo);
        #endregion
    }

    [Fact]
    public void Documentation_CreateTempFile()
    {
        #region FullPath_CreateTempFile
        // Creates an empty file with a unique name in the temporary folder
        FullPath report = FullPath.CreateTempFile(prefix: "report-", suffix: ".csv");

        Assert.True(File.Exists(report));
        Assert.Equal(FullPath.GetTempPath(), report.Parent);
        Assert.StartsWith("report-", report.Name);
        Assert.Equal(".csv", report.Extension);
        #endregion

        File.Delete(report);
    }

    [Fact]
    public void Documentation_NamesAndExtensions()
    {
        #region FullPath_NamesAndExtensions
        FullPath file = FullPath.GetTempPath() / "reports" / "summary.tar.gz";

        Assert.Equal("summary.tar.gz", file.Name);
        Assert.Equal("summary.tar", file.NameWithoutExtension);
        Assert.Equal(".gz", file.Extension);
        Assert.Equal(FullPath.GetTempPath() / "reports", file.Parent);
        #endregion
    }

    [Fact]
    public void Documentation_ChangeNameAndExtension()
    {
        #region FullPath_ChangeNameAndExtension
        FullPath file = FullPath.GetTempPath() / "reports" / "summary.tar.gz";

        // Replace the last extension, all the extensions, or a number of them
        Assert.Equal("summary.tar.zip", file.WithExtension(".zip").Name);
        Assert.Equal("summary.zip", file.WithExtension(".zip", replaceAllTrailingExtensions: true).Name);
        Assert.Equal("summary.zip", file.WithExtension("zip", extensionCount: 2).Name); // the leading dot is optional

        // null removes the extension
        Assert.Equal("summary.tar", file.WithExtension(null).Name);

        // Rename the file and keep its folder
        Assert.Equal(FullPath.GetTempPath() / "reports" / "readme.md", file.WithName("readme.md"));
        Assert.Equal("details.gz", file.WithNameWithoutExtension("details").Name);
        #endregion
    }

    [Fact]
    public void Documentation_ParentOfRoot()
    {
        #region FullPath_ParentOfRoot
        FullPath root = FullPath.FromPath(Path.GetPathRoot(Environment.CurrentDirectory)!);

        // A root folder (/ or C:\) has no parent
        Assert.True(root.Parent.IsEmpty);
        #endregion
    }

    [Fact]
    public void Documentation_Equality()
    {
        #region FullPath_Equality
        FullPath upper = FullPath.GetTempPath() / "Reports" / "summary.txt";
        FullPath lower = FullPath.GetTempPath() / "reports" / "summary.txt";

        // == and Equals ignore case on Windows, macOS, iOS and tvOS, and are case-sensitive elsewhere
        Assert.Equal(!FullPathComparer.Default.IsCaseSensitive, upper == lower);

        // Choose the case sensitivity when the result must not depend on the operating system
        Assert.True(upper.Equals(lower, ignoreCase: true));
        Assert.False(upper.Equals(lower, ignoreCase: false));
        Assert.True(FullPathComparer.CaseInsensitive.Equals(upper, lower));
        #endregion
    }

    [Fact]
    public void Documentation_Collections()
    {
        #region FullPath_Collections
        FullPath root = FullPath.GetTempPath();

        // Use a FullPathComparer to choose the case sensitivity of a set or a dictionary
        var visited = new HashSet<FullPath>(FullPathComparer.CaseInsensitive);
        Assert.True(visited.Add(root / "Data"));
        Assert.False(visited.Add(root / "data"));

        // Sort paths with the same comparers
        List<FullPath> paths = [root / "b.txt", root / "A.txt", root / "a.txt"];
        paths.Sort(FullPathComparer.CaseSensitive);
        Assert.Equal([root / "A.txt", root / "a.txt", root / "b.txt"], paths);
        #endregion
    }

    [Fact]
    public void Documentation_IsChildOf()
    {
        #region FullPath_IsChildOf
        FullPath uploads = FullPath.GetTempPath() / "uploads";

        FullPath image = uploads / "images" / "logo.png";
        Assert.True(image.IsChildOf(uploads));

        // ".." is resolved before the check, so the path cannot escape the folder
        FullPath escaped = uploads / "../secrets.txt";
        Assert.False(escaped.IsChildOf(uploads));

        // A folder is not its own child, and a sibling that starts with the same name is not a child
        Assert.False(uploads.IsChildOf(uploads));
        Assert.False((FullPath.GetTempPath() / "uploads-old").IsChildOf(uploads));
        #endregion
    }

    [Fact]
    public void Documentation_MakePathRelativeTo()
    {
        #region FullPath_MakePathRelativeTo
        FullPath project = FullPath.GetTempPath() / "project";
        FullPath program = project / "src" / "Program.cs";

        Assert.Equal(Path.Combine("src", "Program.cs"), program.MakePathRelativeTo(project));
        Assert.Equal(Path.Combine("..", "..", "docs"), (project / "docs").MakePathRelativeTo(project / "src" / "app"));
        Assert.Equal(".", project.MakePathRelativeTo(project));
        #endregion
    }

    [Fact]
    public void Documentation_FindAncestors()
    {
        #region FullPath_FindAncestors
        FullPath repository = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory());
        Directory.CreateDirectory(repository / ".git");
        FullPath project = repository / "src" / "App";
        Directory.CreateDirectory(project);
        File.WriteAllText(project / "App.csproj", "<Project />");
        FullPath program = project / "Program.cs";

        // The folder that contains .git (a folder, or a file in a worktree)
        Assert.True(program.TryFindGitRepositoryRoot(out FullPath root));
        Assert.Equal(repository, root);

        // The nearest folder that matches a condition
        Assert.True(program.TryFindFirstAncestor(path => Directory.EnumerateFiles(path, "*.csproj").Any(), out FullPath projectFolder));
        Assert.Equal(project, projectFolder);
        #endregion

        Directory.Delete(repository, recursive: true);
    }

    [Fact]
    public void Documentation_SymbolicLinks()
    {
        #region FullPath_SymbolicLinks
        FullPath folder = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory());
        FullPath file = folder / "data.txt";
        File.WriteAllText(file, "");
        FullPath link1 = folder / "link1.txt"; // link1.txt -> data.txt
        File.CreateSymbolicLink(link1, file);
        FullPath link2 = folder / "link2.txt"; // link2.txt -> link1.txt
        File.CreateSymbolicLink(link2, link1);

        Assert.True(link2.IsSymbolicLink());
        Assert.False(file.IsSymbolicLink());

        // The target stored in the link, which can be another link
        Assert.True(link2.TryGetSymbolicLinkTarget(out FullPath? target));
        Assert.Equal(link1, target);

        // Follow the links until the target is not a link
        Assert.True(link2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.FinalTarget, out FullPath? finalTarget));
        Assert.Equal(file, finalTarget);

        // The canonical path also resolves the links in the parent folders
        Assert.True(link2.TryGetCanonicalPath(out FullPath? canonicalLink));
        Assert.True(file.TryGetCanonicalPath(out FullPath? canonicalFile));
        Assert.Equal(canonicalFile, canonicalLink);
        #endregion

        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Documentation_Json()
    {
        #region FullPath_Json
        FullPath output = FullPath.GetTempPath() / "output";

        // A FullPath is written as a JSON string
        string json = JsonSerializer.Serialize(output);
        Assert.Equal(JsonSerializer.Serialize(output.Value), json);
        Assert.Equal(output, JsonSerializer.Deserialize<FullPath>(json));

        // A relative path is resolved against the current directory, and an empty string gives the empty path
        Assert.Equal(FullPath.CurrentDirectory() / "output", JsonSerializer.Deserialize<FullPath>("\"output\""));
        Assert.True(JsonSerializer.Deserialize<FullPath>("\"\"").IsEmpty);
        #endregion
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void Documentation_ReservedDeviceNames()
    {
        #region FullPath_ReservedDeviceNames
        FullPath file = FullPath.FromPath(@"C:\logs\NUL.txt");

        // Value adds the \\?\ prefix, so the file APIs open the file instead of the NUL device
        Assert.Equal(@"\\?\C:\logs\NUL.txt", file.Value);
        Assert.Equal(@"C:\logs\NUL.txt", file.RawValue);
        #endregion
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void Documentation_WindowsExtendedPath()
    {
        #region FullPath_WindowsExtendedPath
        Assert.Equal(@"\\?\C:\temp\test.txt", FullPath.FromPath(@"C:\temp\test.txt").ToWindowsExtendedPath());
        Assert.Equal(@"\\?\UNC\server\share\file.txt", FullPath.FromPath(@"\\server\share\file.txt").ToWindowsExtendedPath());

        // FromPath removes the prefix
        Assert.Equal(@"C:\temp\test.txt", FullPath.FromPath(@"\\?\C:\temp\test.txt").Value);
        Assert.Equal(@"\\server\share\file.txt", FullPath.FromPath(@"\\?\UNC\server\share\file.txt").Value);
        #endregion
    }

    [Fact]
    public void IsEmpty()
    {
        Assert.True(default(FullPath).IsEmpty);
        Assert.True(FullPath.Empty.IsEmpty);
        Assert.False(FullPath.FromPath("test").IsEmpty);
    }

    [Fact]
    public void FromPath_EmptyString_ReturnsEmpty()
    {
        Assert.True(FullPath.FromPath("").IsEmpty);
        Assert.Equal(FullPath.Empty, FullPath.FromPath(""));
        Assert.Equal(FullPath.Empty, FullPath.FromPath(string.Empty));
    }

    [Fact]
    public void FromPath_EmptyString_IsNotTheCurrentDirectory()
    {
        Assert.NotEqual(FullPath.CurrentDirectory(), FullPath.FromPath(""));
        Assert.Equal(FullPath.CurrentDirectory(), FullPath.FromPath("."));
    }

    [Fact]
    public void FromPath_RoundTripsTheStringRepresentation()
    {
        Assert.Equal(FullPath.Empty, FullPath.FromPath(FullPath.Empty.Value));

        var path = FullPath.FromPath("test");
        Assert.Equal(path, FullPath.FromPath(path.Value));
    }

    [Fact]
    public void Properties()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";
        Assert.False(path.IsEmpty);
        Assert.Equal("b.txt", path.Name);
        Assert.Equal(".txt", path.Extension);
        Assert.Equal("b", path.NameWithoutExtension);
        Assert.Equal(FullPath.FromPath("test") / "a", path.Parent);
    }

    [Fact]
    public void AddOperator()
    {
        var actual = FullPath.FromPath("test") + "a" + "b.txt";
        var expected = FullPath.FromPath("testab.txt");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeName()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";
        var newPath = path.WithName("c.txt");
        Assert.Equal(FullPath.FromPath("test") / "a" / "c.txt", newPath);
    }

    [Fact]
    public void ChangeNameWithoutExtension()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";
        var newPath = path.WithNameWithoutExtension("c");
        Assert.Equal(FullPath.FromPath("test") / "a" / "c.txt", newPath);
    }

    [Fact]
    public void ChangeName_NormalizesTheResultingPath()
    {
        var root = FullPath.FromPath("test");
        var path = root / "a" / "b.txt";

        var newPath = path.WithName("../c.txt");

        Assert.Equal(root / "c.txt", newPath);
        Assert.Equal(FullPath.FromPath(newPath.RawValue), newPath);
    }

    [Fact]
    public void ChangeName_EscapingTheRootIsNotReportedAsAChild()
    {
        var root = FullPath.FromPath("test");
        var path = root / "a" / "b.txt";

        var newPath = path.WithName("../../../../etc/passwd");

        Assert.False(newPath.IsChildOf(root));
        Assert.Equal(FullPath.FromPath(newPath.RawValue), newPath);
    }

    [Fact]
    public void ChangeName_RootDirectory()
    {
        var root = GetRootDirectory();

        var newPath = root.WithName("temp");

        Assert.Equal(root / "temp", newPath);
    }

    [Fact]
    public void ChangeName_EmbeddedNullCharacterIsRejected()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";

        Assert.Throws<ArgumentException>(() => path.WithName("a\0b"));
    }

    [Fact]
    public void ChangeNameWithoutExtension_NormalizesTheResultingPath()
    {
        var root = FullPath.FromPath("test");
        var path = root / "a" / "b.txt";

        var newPath = path.WithNameWithoutExtension("../c");

        Assert.Equal(root / "c.txt", newPath);
    }

    [Fact]
    public void ChangeNameWithoutExtension_RootDirectory()
    {
        var root = GetRootDirectory();

        var newPath = root.WithNameWithoutExtension("temp");

        Assert.Equal(root / "temp", newPath);
    }

    [Fact]
    public void ChangeExtension_NormalizesTheResultingPath()
    {
        var root = FullPath.FromPath("test");
        var path = root / "a" / "b.txt";

        var newPath = path.WithExtension("/../../c");

        Assert.Equal(FullPath.FromPath(newPath.RawValue), newPath);
        Assert.False(newPath.IsChildOf(root / "a"));
    }

    [Fact]
    public void ChangeExtension_DotFileKeepsNoTrailingSeparator()
    {
        var parent = FullPath.FromPath("test") / "a";
        var path = parent / ".gitignore";

        var newPath = path.WithExtension(null);

        Assert.Equal(parent, newPath);
        Assert.Equal(parent.RawValue, newPath.RawValue);
    }

    [Fact]
    public void ChangeMultipleExtensions_DotFileKeepsNoTrailingSeparator()
    {
        var parent = FullPath.FromPath("test") / "a";
        var path = parent / ".gitignore";

        var newPath = path.WithExtension(null, replaceAllTrailingExtensions: true);

        Assert.Equal(parent, newPath);
        Assert.Equal(parent.RawValue, newPath.RawValue);
    }

    [Fact]
    public void ChangeMultipleExtensions()
    {
        var path = FullPath.FromPath("test") / "a" / "b.tar.gz";
        var newPath = path.WithExtension(".zip", replaceAllTrailingExtensions: true);
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.zip", newPath);
    }

    [Fact]
    public void ChangeExtensionWithMultipleDots()
    {
        var path = FullPath.FromPath("test") / "a" / "b.tar.gz";
        var newPath = path.WithExtension(".zip", extensionCount: 2);
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.zip", newPath);
    }

    [Fact]
    public void ChangeMultipleExtensions_WithCount()
    {
        var path = FullPath.FromPath("test") / "a" / "b.tar.gz";
        var newPath = path.WithExtension(".zip", extensionCount: 1);
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.tar.zip", newPath);
    }

    [Fact]
    public void ChangeMultipleExtensions_NoExtension()
    {
        var path = FullPath.FromPath("test") / "a" / "b";
        var newPath = path.WithExtension(".zip", replaceAllTrailingExtensions: true);
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.zip", newPath);
    }

    [Fact]
    public void ChangeMultipleExtensions_InvalidCount()
    {
        var path = FullPath.FromPath("test") / "a" / "b.tar.gz";
        Assert.Throws<ArgumentOutOfRangeException>(() => path.WithExtension(".zip", extensionCount: 0));
    }

    [Fact]
    public void ChangeMultipleExtensions_CountBeyondTheAvailableExtensions()
    {
        var path = FullPath.FromPath("test") / "a" / "b.gz";
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.zip", path.WithExtension(".zip", extensionCount: 5));
    }

    [Fact]
    public void ChangeMultipleExtensions_ConsecutiveDotsCountOneEach()
    {
        var path = FullPath.FromPath("test") / "a" / "b..gz";

        Assert.Equal(FullPath.FromPath("test") / "a" / "b..zip", path.WithExtension(".zip", extensionCount: 1));
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.zip", path.WithExtension(".zip", extensionCount: 2));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void ChangeMultipleExtensions_TrailingDotIsAnEmptyExtension()
    {
        // Windows path normalization eats a trailing dot, so the name only survives on Unix
        var path = FullPath.FromPath("test") / "a" / "archive.tar.";

        // Path.ChangeExtension replaces the empty extension the trailing dot stands for, and every removal here has to
        // behave the same way, so only the number of removed extensions changes with the count
        Assert.Equal("archive.tar.zip", path.WithExtension("zip").Name);
        Assert.Equal("archive.zip", path.WithExtension("zip", extensionCount: 2).Name);
        Assert.Equal("archive.zip", path.WithExtension("zip", replaceAllTrailingExtensions: true).Name);
        Assert.Equal("archive", path.WithExtension(null, extensionCount: 2).Name);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void ChangeMultipleExtensions_EmptyExtensionKeepsTheDot()
    {
        // Windows path normalization eats a trailing dot, so the name only survives on Unix
        var path = FullPath.FromPath("test") / "a" / "file.txt";

        // An empty extension is not the same as no extension, which is what null asks for
        Assert.Equal("file.", path.WithExtension("").Name);
        Assert.Equal("file.", path.WithExtension("", extensionCount: 2).Name);
        Assert.Equal("file.", path.WithExtension("", replaceAllTrailingExtensions: true).Name);
        Assert.Equal("file", path.WithExtension(null, extensionCount: 2).Name);
    }

    [Fact]
    public void CombinePath()
    {
        var actual = FullPath.FromPath("test") / "a" / ".." / "a" / "." / "b";
        var expected = FullPath.Combine("test", "a", "b");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CombinePath_ReadOnlySpan()
    {
        var actual = FullPath.FromPath("test") / "a" / ".." / "a" / "." / "b";
        var expected = FullPath.Combine(FullPath.FromPath("test"), (ReadOnlySpan<string>)["a", "b"]);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Combine_AllOverloadsAgree()
    {
        var root = FullPath.FromPath("test");
        var expected = root / "a" / "b" / "c";
        string[] parts = ["a", "b", "c"];
        string[] rootedParts = ["test", "a", "b", "c"];

        Assert.Equal(expected, FullPath.Combine("test", "a", "b", "c"));
        Assert.Equal(expected, FullPath.Combine(rootedParts));
        Assert.Equal(expected, FullPath.Combine((ReadOnlySpan<string>)rootedParts));
        Assert.Equal(expected, FullPath.Combine(root, "a", "b", "c"));
        Assert.Equal(expected, FullPath.Combine(root, parts));
        Assert.Equal(expected, FullPath.Combine(root, (ReadOnlySpan<string>)parts));
    }

    [Fact]
    public void Combine_ShorterOverloadsAgree()
    {
        var root = FullPath.FromPath("test");

        Assert.Equal(root / "a", FullPath.Combine("test", "a"));
        Assert.Equal(root / "a", FullPath.Combine(root, "a"));
        Assert.Equal(root / "a" / "b", FullPath.Combine("test", "a", "b"));
        Assert.Equal(root / "a" / "b", FullPath.Combine(root, "a", "b"));
    }

    [Fact]
    public void Combine_EmptyRootResolvesAgainstTheCurrentDirectory()
    {
        // Documents current behaviour: every FullPath-rooted overload falls back to FromPath, which resolves a
        // relative path against Environment.CurrentDirectory rather than propagating Empty or throwing.
        var cwd = FullPath.CurrentDirectory();
        string[] parts = ["a", "b"];

        Assert.Equal(cwd / "a", FullPath.Combine(FullPath.Empty, "a"));
        Assert.Equal(cwd / "a" / "b", FullPath.Combine(FullPath.Empty, "a", "b"));
        Assert.Equal(cwd / "a" / "b" / "c", FullPath.Combine(FullPath.Empty, "a", "b", "c"));
        Assert.Equal(cwd / "a" / "b", FullPath.Combine(FullPath.Empty, parts));
        Assert.Equal(cwd / "a" / "b", FullPath.Combine(FullPath.Empty, (ReadOnlySpan<string>)parts));
        Assert.Equal(cwd / "a", FullPath.Empty / "a");
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a b", "a b")]
    [InlineData("a/", "a")]
    [InlineData("a/../b", "b")]
    [InlineData(".", ".")]
    [InlineData("..", "..")]
    [InlineData("../..", "../..")]
    [InlineData("../sibling", "../sibling")]
    public void MakeRelativeTo(string childPath, string expected)
    {
        var rootPath = FullPath.FromPath("test");
        var path1 = FullPath.Combine("test", childPath);
        Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar), path1.MakePathRelativeTo(rootPath));
    }

    [Fact]
    public void MakeRelativeTo_RootDirectory()
    {
        var rootPath = GetRootDirectory();
        var path = rootPath / "a";

        Assert.Equal("a", path.MakePathRelativeTo(rootPath));
    }

    [Theory]
    [InlineData("test", "test/a")]
    [InlineData("test", "test/a.txt")]
    [InlineData("test", "test/b/a.txt")]
    public void IsChildOf_True(string root, string path)
    {
        var rootPath = FullPath.FromPath(root);
        var childPath = FullPath.Combine(root, path);
        Assert.True(childPath.IsChildOf(rootPath));
    }

    [Theory]
    [InlineData("test/", "test")]
    [InlineData("test/", "test/")]
    [InlineData("test", "test")]
    [InlineData("test", "test/")]
    [InlineData("test", "abc")]
    [InlineData("test", "../test")]
    [InlineData("test", "test1/b/a.txt")]
    public void IsChildOf_False(string root, string path)
    {
        var rootPath = FullPath.FromPath(root);
        var childPath = FullPath.FromPath(path);
        Assert.False(childPath.IsChildOf(rootPath));
    }

    [Fact]
    public void IsChildOf_RootDirectory()
    {
        var rootPath = GetRootDirectory();

        Assert.True((rootPath / "a").IsChildOf(rootPath));
        Assert.True((rootPath / "a" / "b.txt").IsChildOf(rootPath));
        Assert.False(rootPath.IsChildOf(rootPath));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows | TestOperatingSystems.MacOS)]
    public void IsChildOf_IgnoresCaseOnWindowsAndMacOS()
    {
        var rootPath = FullPath.FromPath("test");
        var childPath = FullPath.FromPath("TEST") / "a.txt";

        Assert.True(childPath.IsChildOf(rootPath));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux)]
    public void IsChildOf_IsCaseSensitiveOnLinux()
    {
        var rootPath = FullPath.FromPath("test");
        var childPath = FullPath.FromPath("TEST") / "a.txt";

        Assert.False(childPath.IsChildOf(rootPath));
    }

    [Theory]
    [InlineData("test", "abc")]
    [InlineData("test", "../test")]
    [InlineData("test", "test1/b/a.txt")]
    public void Equals_False(string root, string path)
    {
        var rootPath = FullPath.FromPath(root);
        var childPath = FullPath.FromPath(path);

        Assert.NotEqual(childPath, rootPath);
    }

    [Theory]
    [InlineData("test/", "test")]
    [InlineData("test/", "test/")]
    [InlineData("test", "test")]
    [InlineData("test", "test/")]
    [InlineData("test", "./test")]
    public void Equals_True(string root, string path)
    {
        var rootPath = FullPath.FromPath(root);
        var childPath = FullPath.FromPath(path);
        Assert.Equal(childPath, rootPath);
    }

    [Fact]
    public void FullPathComparer_CaseSensitive()
    {
        var lower = FullPath.FromPath("test") / "a.txt";
        var upper = FullPath.FromPath("test") / "A.TXT";

        Assert.True(FullPathComparer.CaseSensitive.IsCaseSensitive);
        Assert.False(FullPathComparer.CaseSensitive.Equals(lower, upper));
        Assert.NotEqual(0, FullPathComparer.CaseSensitive.Compare(lower, upper));
    }

    [Fact]
    public void FullPathComparer_CaseInsensitive()
    {
        var lower = FullPath.FromPath("test") / "a.txt";
        var upper = FullPath.FromPath("test") / "A.TXT";

        Assert.False(FullPathComparer.CaseInsensitive.IsCaseSensitive);
        Assert.True(FullPathComparer.CaseInsensitive.Equals(lower, upper));
        Assert.Equal(0, FullPathComparer.CaseInsensitive.Compare(lower, upper));
        Assert.Equal(FullPathComparer.CaseInsensitive.GetHashCode(lower), FullPathComparer.CaseInsensitive.GetHashCode(upper));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows | TestOperatingSystems.MacOS)]
    public void FullPathComparer_DefaultIgnoresCaseOnWindowsAndMacOS()
    {
        // Compared through the comparer rather than Assert.Equal: FullPath converts implicitly to string, so
        // Assert.Equal would bind to the string overload and compare ordinally instead of using FullPath equality.
        Assert.False(FullPathComparer.Default.IsCaseSensitive);
        Assert.True(FullPathComparer.Default.Equals(FullPath.FromPath("test") / "a.txt", FullPath.FromPath("test") / "A.TXT"));
        Assert.True((FullPath.FromPath("test") / "a.txt") == (FullPath.FromPath("test") / "A.TXT"));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux)]
    public void FullPathComparer_DefaultIsCaseSensitiveOnLinux()
    {
        Assert.True(FullPathComparer.Default.IsCaseSensitive);
        Assert.False(FullPathComparer.Default.Equals(FullPath.FromPath("test") / "a.txt", FullPath.FromPath("test") / "A.TXT"));
        Assert.False((FullPath.FromPath("test") / "a.txt") == (FullPath.FromPath("test") / "A.TXT"));
    }

    [Fact]
    public void FullPathComparer_Compare()
    {
        var a = FullPath.FromPath("test") / "a.txt";
        var b = FullPath.FromPath("test") / "b.txt";

        Assert.True(FullPathComparer.CaseSensitive.Compare(a, b) < 0);
        Assert.True(FullPathComparer.CaseSensitive.Compare(b, a) > 0);
        Assert.Equal(0, FullPathComparer.CaseSensitive.Compare(a, a));
    }

    [Fact]
    public void FullPathComparer_Empty()
    {
        Assert.Equal(0, FullPathComparer.Default.GetHashCode(FullPath.Empty));
        Assert.Equal(0, FullPathComparer.CaseSensitive.GetHashCode(FullPath.Empty));
        Assert.Equal(0, FullPathComparer.CaseInsensitive.GetHashCode(FullPath.Empty));
        Assert.True(FullPathComparer.Default.Equals(FullPath.Empty, FullPath.Empty));
        Assert.False(FullPathComparer.Default.Equals(FullPath.Empty, FullPath.FromPath("test")));
    }

    [Fact]
    public void EqualsAndCompareTo_IgnoreCase()
    {
        var lower = FullPath.FromPath("test") / "a.txt";
        var upper = FullPath.FromPath("test") / "A.TXT";

        Assert.True(lower.Equals(upper, ignoreCase: true));
        Assert.False(lower.Equals(upper, ignoreCase: false));
        Assert.Equal(lower.GetHashCode(ignoreCase: true), upper.GetHashCode(ignoreCase: true));
        Assert.Equal(0, lower.CompareTo(upper, ignoreCase: true));
        Assert.NotEqual(0, lower.CompareTo(upper, ignoreCase: false));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void FromPath_ExtendedPrefixIsRemovedOnWindows()
    {
        Assert.Equal(@"C:\temp\a.txt", FullPath.FromPath(@"\\?\C:\temp\a.txt").RawValue);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void FromPath_ExtendedUncPrefixIsConvertedBackToAUncPath()
    {
        Assert.Equal(@"\\server\share\folder\file.txt", FullPath.FromPath(@"\\?\UNC\server\share\folder\file.txt").RawValue);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void FromPath_ExtendedPrefixIsAFileNameOnUnix()
    {
        // '\' is a regular file name character on Unix, so this is a relative file name, not a device path
        var expected = FullPath.CurrentDirectory() / @"\\?\a";

        Assert.Equal(expected, FullPath.FromPath(@"\\?\a"));
        Assert.Equal(@"\\?\a", FullPath.FromPath(@"\\?\a").Name);
    }

    [Fact]
    public void JsonDeserialize_InvalidPathThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FullPath>("\"a\\u0000b\""));

        Assert.IsAssignableTo<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void JsonSerialize_RoundTripEmpty()
    {
        var value = FullPath.Empty;
        Assert.Equal(value, JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(value)));
    }

    [Fact]
    public void JsonSerialize_RoundTripNonEmpty()
    {
        var value = FullPath.FromPath(@"c:\test");
        Assert.Equal(value, JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(value)));
    }

    [Fact]
    public void JsonSerialize_Empty()
    {
        Assert.Equal("\"\"", JsonSerializer.Serialize(FullPath.Empty));
    }

    [Fact]
    public void JsonSerialize_NonEmpty()
    {
        var path = Environment.CurrentDirectory;
        Assert.Equal(JsonSerializer.Serialize(path), JsonSerializer.Serialize(FullPath.FromPath(path)));
        Assert.Equal(path, JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(FullPath.FromPath(path))).Value);
    }

    [Fact]
    public void JsonDeserialize_Null()
    {
        Assert.Equal(FullPath.Empty, JsonSerializer.Deserialize<FullPath>(@"null"));
    }

    [Fact]
    public void JsonDeserialize_Empty()
    {
        Assert.Equal(FullPath.Empty, JsonSerializer.Deserialize<FullPath>(@""""""));
    }

    [Fact]
    public void JsonDeserialize_NonEmpty()
    {
        Assert.Equal(FullPath.FromPath(@"c:\test"), JsonSerializer.Deserialize<FullPath>(@"""c:\\test"""));
    }

    [Fact]
    public void IComparable_CompareTo()
    {
        IComparable path = FullPath.FromPath("test") / "a";

        Assert.Equal(0, path.CompareTo(FullPath.FromPath("test") / "a"));
        Assert.True(path.CompareTo(FullPath.FromPath("test") / "b") < 0);
        Assert.True(path.CompareTo(null) > 0);
        Assert.Throws<ArgumentException>(() => path.CompareTo("test"));
    }

    [Fact]
    public async Task ResolveSymlink_FileAbsolutePath()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateEmptyFile("a.txt");
        Assert.False(path.IsSymbolicLink());
        Assert.False(path.TryGetSymbolicLinkTarget(out _));

        // Create symlink
        var symlink = temp.GetFullPath("b.txt");
        CreateSymlink(symlink, path, isDirectory: false);
        Assert.True(File.Exists(symlink));
        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
        Assert.Equal(path, target);
    }

    [Fact]
    public async Task ResolveSymlink_FileRelativePath()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateEmptyFile("a.txt");
        Assert.False(path.IsSymbolicLink());
        Assert.False(path.TryGetSymbolicLinkTarget(out _));

        // Create symlink
        var symlink = temp.GetFullPath("b.txt");
        CreateSymlink(symlink, "a.txt", isDirectory: false);
        Assert.True(File.Exists(symlink));
        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
        Assert.Equal(path, target);
    }

    [Fact]
    public async Task ResolveSymlink_DirectoryAbsolutePath()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateDirectory("a");
        Assert.False(path.IsSymbolicLink());
        Assert.False(path.TryGetSymbolicLinkTarget(out _));

        // Create symlink
        var symlink = temp.GetFullPath("b");
        CreateSymlink(symlink, path, isDirectory: true);
        Assert.True(Directory.Exists(symlink));
        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
        Assert.Equal(path, target);
    }

    [Fact]
    public async Task ResolveSymlink_DirectoryRelativePath()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateDirectory("a");
        Assert.False(path.IsSymbolicLink());
        Assert.False(path.TryGetSymbolicLinkTarget(out _));

        // Create symlink
        var symlink = temp.GetFullPath("b");
        CreateSymlink(symlink, "a", isDirectory: true);
        Assert.True(Directory.Exists(symlink));
        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
        Assert.Equal(path, target);
    }

    [Fact]
    public async Task ResolveSymlink_NonAsciiPath()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateEmptyFile("日本語.txt");

        var symlink = temp.GetFullPath("リンク.txt");
        CreateSymlink(symlink, "日本語.txt", isDirectory: false);

        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
        Assert.Equal(path, target);

        Assert.True(path.TryGetCanonicalPath(out var expected));
        Assert.True(symlink.TryGetCanonicalPath(out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ResolveSymlink_Recursive()
    {
        await using var temp = TemporaryDirectory.Create();
        var file = temp.CreateEmptyFile("a/b.txt");
        var symlink = temp.GetFullPath("c");
        var symlink2 = temp.GetFullPath("d");
        CreateSymlink(symlink, file, isDirectory: false);
        CreateSymlink(symlink2, symlink, isDirectory: false);
        Assert.True(symlink2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.Immediate, out var resolved1));
        Assert.Equal(symlink, resolved1);
        Assert.True(symlink2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out var resolved2));
        Assert.EndsWith(Path.Combine("a", "b.txt"), resolved2.Value.Value); // On GitHub Actions, path starts with a symlink, so resolved2 != file
    }

    [Fact]
    public async Task ResolveSymlink_Cycle()
    {
        await using var temp = TemporaryDirectory.Create();
        var a = temp.GetFullPath("a");
        var b = temp.GetFullPath("b");
        CreateSymlink(a, "b", isDirectory: false);
        CreateSymlink(b, "a", isDirectory: false);

        Assert.True(a.IsSymbolicLink());
        Assert.True(a.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.Immediate, out var immediate));
        Assert.Equal(b, immediate);

        Assert.Throws<IOException>(() => { a.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.FinalTarget, out _); });
        Assert.Throws<IOException>(() => { a.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out _); });
    }

    [Fact]
    public async Task ResolveSymlink_CycleThroughADirectoryLink()
    {
        // The links reference each other through a directory component, so the walk alternates between resolving a
        // link and consuming a component that is not one
        await using var temp = TemporaryDirectory.Create();
        var a = temp.GetFullPath("a");
        var b = temp.GetFullPath("b");
        CreateSymlink(a, Path.Combine("b", "c"), isDirectory: true);
        CreateSymlink(b, "a", isDirectory: true);

        Assert.Throws<IOException>(() => { a.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out _); });
    }

    [Fact]
    public async Task ResolveSymlink_ResolveAllSymbolicLinks()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateDirectory("a/b");
        var symlink = temp.GetFullPath("c");
        CreateSymlink(symlink, path, isDirectory: true);
        var file = temp.CreateEmptyFile("c/d.txt");
        Assert.True(file.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out var resolved));

        Assert.EndsWith(Path.Combine("a", "b", "d.txt"), resolved.Value.Value); // On GitHub Actions, path starts with a symlink, so resolved2 != file
    }

    [Fact]
    public async Task ResolveSymlink_RelativeTargetWithParentSegmentBelowALinkedDirectory()
    {
        // real/sub/link -> ../target, reached through alias -> real/sub. Collapsing "alias/../target" lexically names
        // <root>/target, which is a different file than the one opening the link reads.
        await using var temp = TemporaryDirectory.Create();
        temp.CreateDirectory("real/sub");
        var expected = temp.CreateTextFile("real/target", "actual target");
        temp.CreateTextFile("target", "wrong target");
        CreateSymlink(temp.GetFullPath("alias"), Path.Combine("real", "sub"), isDirectory: true);
        CreateSymlink(temp.GetFullPath("real/sub/link"), Path.Combine("..", "target"), isDirectory: false);

        var link = temp.GetFullPath("alias/link");
        Assert.Equal("actual target", File.ReadAllText(link.Value));
        Assert.True(expected.TryGetCanonicalPath(out var canonicalExpected));

        foreach (var mode in Enum.GetValues<SymbolicLinkResolutionMode>())
        {
            Assert.True(link.TryGetSymbolicLinkTarget(mode, out var target));
            Assert.Equal(canonicalExpected.Value, target.Value);
            Assert.Equal("actual target", File.ReadAllText(target.Value.Value));
        }

        Assert.True(link.TryGetCanonicalPath(out var canonical));
        Assert.Equal(canonicalExpected.Value, canonical.Value);
    }

    [Fact]
    public async Task TryGetCanonicalPath_File()
    {
        await using var temp = TemporaryDirectory.Create();
        var file = temp.CreateEmptyFile("a.txt");

        Assert.True(file.TryGetCanonicalPath(out var canonicalPath));
        Assert.True(File.Exists(canonicalPath));
    }

    [Fact]
    public async Task TryGetCanonicalPath_SymbolicLink()
    {
        await using var temp = TemporaryDirectory.Create();
        var target = temp.CreateEmptyFile("a.txt");
        var symlink = temp.GetFullPath("b.txt");
        CreateSymlink(symlink, "a.txt", isDirectory: false);

        Assert.True(target.TryGetCanonicalPath(out var expected));
        Assert.True(symlink.TryGetCanonicalPath(out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TryGetCanonicalPath_MissingPath()
    {
        await using var temp = TemporaryDirectory.Create();
        var missingPath = temp.GetFullPath("missing.txt");

        Assert.False(missingPath.TryGetCanonicalPath(out _));
    }

    [Fact]
    public void ChangeExtension()
    {
        var actual = FullPath.FromPath("test.a.txt").WithExtension(".avi");
        var expected = FullPath.FromPath("test.a.avi");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_NoExtension()
    {
        var actual = FullPath.FromPath("test").WithExtension(".avi");
        var expected = FullPath.FromPath("test.avi");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_Empty()
    {
        var actual = FullPath.Empty.WithExtension(".avi");
        var expected = FullPath.Empty;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_Null()
    {
        var actual = FullPath.FromPath("test").WithExtension(null);
        var expected = FullPath.FromPath("test");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_NoDot()
    {
        var actual = FullPath.FromPath("test.txt").WithExtension("avi");
        var expected = FullPath.FromPath("test.avi");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CreateTempFile_Default()
    {
        var path = FullPath.CreateTempFile(prefix: null);
        try
        {
            Assert.Equal(".tmp", path.Extension);
            Assert.True(File.Exists(path.Value));
        }
        finally
        {
            File.Delete(path.Value);
        }
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void CreateTempFile_IsOnlyAccessibleByTheCurrentUser()
    {
        var path = FullPath.CreateTempFile(prefix: null);
        try
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path.Value));
        }
        finally
        {
            File.Delete(path.Value);
        }
    }

    [Fact]
    public void CreateTempFile_WithFolderPrefixSuffix()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var folder = tempDirectory.FullPath / Guid.NewGuid().ToString("N");
        Assert.False(Directory.Exists(folder.Value));

        var path = FullPath.CreateTempFile(folder, "prefix-", ".txt");

        try
        {
            Assert.Equal(".txt", path.Extension);
            Assert.Equal(folder, path.Parent);
            Assert.StartsWith("prefix-", path.Name);
            Assert.True(File.Exists(path.Value));
            Assert.True(Directory.Exists(folder.Value));
        }
        finally
        {
            File.Delete(path.Value);
        }
    }

    [Fact]
    public void CreateTempFile_WithNullSuffix()
    {
        var path = FullPath.CreateTempFile(prefix: "prefix-", suffix: null);
        try
        {
            Assert.Equal(string.Empty, path.Extension);
            Assert.StartsWith("prefix-", path.Name);
            Assert.True(File.Exists(path.Value));
        }
        finally
        {
            File.Delete(path.Value);
        }
    }

    [Fact]
    public void CreateTempFile_ThrowsAfterMaxAttempts()
    {
        var folder = FullPath.GetTempPath();

        // Longer than the maximum length of a file name, so every attempt fails while the affix stays a valid fragment
        var tooLongSuffix = new string('a', 300);
        var exception = Assert.Throws<IOException>(() => FullPath.CreateTempFile(folder, prefix: null, suffix: tooLongSuffix));
        Assert.Contains("10 attempts", exception.Message);
    }

    [Theory]
    [InlineData("../outside-")]
    [InlineData("sub/")]
    [InlineData("\0")]
    public void CreateTempFile_ThrowsWhenThePrefixIsNotAFileName(string prefix)
    {
        var exception = Assert.Throws<ArgumentException>(() => FullPath.CreateTempFile(FullPath.GetTempPath(), prefix, suffix: ".tmp"));
        Assert.Equal("prefix", exception.ParamName);
    }

    [Theory]
    [InlineData("/../fixed.txt")]
    [InlineData("/outside.txt")]
    public void CreateTempFile_ThrowsWhenTheSuffixIsNotAFileName(string suffix)
    {
        var exception = Assert.Throws<ArgumentException>(() => FullPath.CreateTempFile(FullPath.GetTempPath(), prefix: null, suffix));
        Assert.Equal("suffix", exception.ParamName);
    }

    [Fact]
    public async Task CreateTempFile_RejectedAffixCreatesNothing()
    {
        await using var temp = TemporaryDirectory.Create();
        var folder = temp.GetFullPath("destination");

        // The affix would otherwise put the file next to the destination folder rather than inside it
        Assert.Throws<ArgumentException>(() => FullPath.CreateTempFile(folder, prefix: "../outside-"));

        Assert.False(Directory.Exists(folder));
        Assert.Empty(Directory.GetFileSystemEntries(temp.FullPath));
    }

    [Fact]
    public void GetFolderPath()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(FullPath.FromPath(expected), FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    [Fact]
    public void GetFolderPath_WithSpecialFolderOption()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        Assert.Equal(FullPath.FromPath(expected), FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));
    }

    [Fact]
    public async Task TryFindFirstAncestorOrSelf()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var fileName = Guid.NewGuid().ToString("N");
        var filePath = tempDir.CreateEmptyFile(fileName);

        Assert.False(tempDir.FullPath.TryFindFirstAncestorOrSelf(p => false, out _));

        Assert.True(tempDir.FullPath.TryFindFirstAncestorOrSelf(p => File.Exists(p / fileName), out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public async Task TryFindFirstAncestorOrSelf_Depth()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var fileName = Guid.NewGuid().ToString("N");
        var filePath = tempDir.CreateEmptyFile(fileName);
        var subDir = tempDir.CreateDirectory("a/b/c/d/e");

        Assert.False(subDir.TryFindFirstAncestorOrSelf(p => false, out _));

        Assert.True(subDir.TryFindFirstAncestorOrSelf(p => File.Exists(p / fileName), out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public async Task TryFindFirstAncestor_ExcludesSelf()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var fileName = Guid.NewGuid().ToString("N");
        tempDir.CreateEmptyFile(fileName);
        var subDir = tempDir.CreateDirectory("a");

        // The predicate matches the starting directory itself, which is the only thing that distinguishes the two
        Assert.True(tempDir.FullPath.TryFindFirstAncestorOrSelf(p => File.Exists(p / fileName), out var withSelf));
        Assert.Equal(tempDir.FullPath, withSelf);
        Assert.False(tempDir.FullPath.TryFindFirstAncestor(p => File.Exists(p / fileName), out _));

        Assert.True(subDir.TryFindFirstAncestor(p => File.Exists(p / fileName), out var fromChild));
        Assert.Equal(tempDir.FullPath, fromChild);
    }

    [Fact]
    public async Task TryFindFirstAncestor_NoMatch()
    {
        await using var tempDir = TemporaryDirectory.Create();

        Assert.False(tempDir.FullPath.TryFindFirstAncestor(p => false, out var result));
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void TryFindGitRepositoryRoot()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateDirectory(".git");
        var subDir = tempDir.CreateDirectory("src/app");

        Assert.True(subDir.TryFindGitRepositoryRoot(out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void TryFindGitRepositoryRoot_Worktree()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateTextFile(".git", "gitdir: C:/main-repo/.git/worktrees/sample-worktree");
        var subDir = tempDir.CreateDirectory("src/app");

        Assert.True(subDir.TryFindGitRepositoryRoot(out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void TryFindGitRepositoryRoot_Worktree_FromFilePath()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateTextFile(".git", "gitdir: C:/main-repo/.git/worktrees/sample-worktree");
        var filePath = tempDir.CreateEmptyFile("src/app/readme.txt");

        Assert.True(filePath.TryFindGitRepositoryRoot(out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void TryFindGitRepositoryRoot_NotFound()
    {
        using var tempDir = TemporaryDirectory.Create();
        var subDir = tempDir.CreateDirectory("src/app");

        Assert.False(subDir.TryFindGitRepositoryRoot(out _));
    }

    [Fact]
    public void FindRequiredGitRepositoryRoot()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateDirectory(".git");
        var subDir = tempDir.CreateDirectory("src/app");

        var result = subDir.FindRequiredGitRepositoryRoot();

        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void FindRequiredGitRepositoryRoot_Worktree()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateTextFile(".git", "gitdir: C:/main-repo/.git/worktrees/sample-worktree");
        var subDir = tempDir.CreateDirectory("src/app");

        var result = subDir.FindRequiredGitRepositoryRoot();

        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void FindRequiredGitRepositoryRoot_NotFound()
    {
        using var tempDir = TemporaryDirectory.Create();
        var subDir = tempDir.CreateDirectory("src/app");

        Assert.Throws<InvalidOperationException>(() => subDir.FindRequiredGitRepositoryRoot());
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void KnownFolderTest()
    {
        var fullPath = FullPath.GetKnownFolderPath(KnownFolder.Downloads);
        Assert.NotEmpty(fullPath.Value);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void KnownFolderExample()
    {
        #region KnownFolder_Example
        // Get path to Downloads folder
        FullPath downloadsPath = FullPath.GetKnownFolderPath(KnownFolder.Downloads);
        Assert.False(downloadsPath.IsEmpty);

        // Get path to Documents folder
        FullPath documentsPath = FullPath.GetKnownFolderPath(KnownFolder.Documents);
        Assert.False(documentsPath.IsEmpty);
        #endregion
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void ToWindowsExtendedPath_ThrowsOnNonWindows()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";

        Assert.Throws<PlatformNotSupportedException>(path.ToWindowsExtendedPath);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_RegularPath()
    {
        var path = FullPath.FromPath(@"C:\temp\test.txt");
        var extended = path.ToWindowsExtendedPath();
        Assert.Equal(@"\\?\C:\temp\test.txt", extended);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_UNCPath()
    {
        var path = FullPath.FromPath(@"\\server\share\folder\file.txt");
        var extended = path.ToWindowsExtendedPath();
        Assert.Equal(@"\\?\UNC\server\share\folder\file.txt", extended);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_AlreadyExtended()
    {
        var path = FullPath.FromPath(@"C:\temp\test.txt");
        var extended = path.ToWindowsExtendedPath();

        // FromPath removes the device prefix, so the round-trip has to give back the original path. Asserting only
        // that ToWindowsExtendedPath is idempotent would compare two identical expressions and could never fail.
        Assert.Equal(path, FullPath.FromPath(extended));
        Assert.Equal(extended, FullPath.FromPath(extended).ToWindowsExtendedPath());
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_UNCPath_RoundTrips()
    {
        var path = FullPath.FromPath(@"\\server\share\folder\file.txt");
        var extended = path.ToWindowsExtendedPath();

        Assert.Equal(@"\\?\UNC\server\share\folder\file.txt", extended);
        Assert.Equal(path, FullPath.FromPath(extended));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_Empty()
    {
        var extended = FullPath.Empty.ToWindowsExtendedPath();
        Assert.Equal("", extended);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_LongPath()
    {
        var longSegment = new string('a', 250);
        var path = FullPath.FromPath($@"C:\{longSegment}\test.txt");
        var extended = path.ToWindowsExtendedPath();
        Assert.StartsWith(@"\\?\", extended);
        Assert.Contains(longSegment, extended);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void FromPath_NTExtendedUncPath()
    {
        // A reparse point stores an absolute target in the NT form, so FromPath has to give it back its UNC root
        Assert.Equal(FullPath.FromPath(@"\\server\share\folder\file.txt"), FullPath.FromPath(@"\??\UNC\server\share\folder\file.txt"));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void FromPath_NTExtendedDrivePath()
    {
        Assert.Equal(FullPath.FromPath(@"C:\temp\test.txt"), FullPath.FromPath(@"\??\C:\temp\test.txt"));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void FromPath_VolumeGuidPathKeepsItsRoot()
    {
        // A volume GUID path has no ordinary spelling, so dropping the prefix would leave a relative path that
        // Path.GetFullPath resolves against the current directory
        const string VolumePath = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\folder\file.txt";

        Assert.Equal(VolumePath, FullPath.FromPath(VolumePath).RawValue);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void FromPath_NTVolumeGuidPathUsesTheWin32Spelling()
    {
        Assert.Equal(
            @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\folder\file.txt",
            FullPath.FromPath(@"\??\Volume{12345678-1234-1234-1234-123456789abc}\folder\file.txt").RawValue);
    }

    private static FullPath GetRootDirectory()
    {
        return FullPath.FromPath(Path.GetPathRoot(FullPath.CurrentDirectory().Value)!);
    }

    private static void CreateSymlink(FullPath source, string target, bool isDirectory)
    {
        if (isDirectory)
        {
            Directory.CreateSymbolicLink(source, target);
        }
        else
        {
            File.CreateSymbolicLink(source, target);
        }
    }
}
