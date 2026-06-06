# Meziantou.Framework.FullPath

`FullPath` ensures you always deal with full path in your application and provides many common methods to manipulate paths.

````c#
// Create FullPath
FullPath rootPath = FullPath.FromPath("demo"); // It automatically calls Path.GetFullPath to resolve the path
FullPath filePath = FullPath.Combine(rootPath, "temp", "meziantou.txt"); // Use Path.Combine to join paths (you can combine as many path as you needed)
FullPath temp = FullPath.GetTempPath(); // equivalent of Path.GetTempPath()
FullPath cwd = FullPath.GetCurrentDirectory(); // equivalent of Environment.CurrentDirectory

// Combine path: you can use the / operator to join path
FullPath filePath1 = rootPath / "temp" / "meziantou.txt";
// Or use the + operator to append a suffix to the current path
FullPath backupFilePath = filePath1 + ".bak";

// Compare path
// Comparisons are case-insensitive on Windows and case-sensitive on other operating systems by default
_ = filePath == rootPath;
_ = filePath.Equals(rootPath, ignoreCase: false);

// Get parent directory
FullPath parent = filePath.Parent;

// Get file/directory name - extension
var name = filePath.Name;
var ext = filePath.Extension;
FullPath pathWithExtension = filePath.WithExtension(".log");
FullPath pathWithAllExtensionsChanged = FullPath.FromPath("archive.tar.gz").WithExtensions(".zip");
FullPath pathWithNewName = filePath.WithName("other.txt");
FullPath pathWithNewNameWithoutExtension = filePath.WithNameWithoutExtension("other");

// Make relative path
string relativePath = filePath.MakeRelativeTo(rootPath); // temp\meziantou.txt

// Check if a path is under another path
bool isChildOf = filePath.IsChildOf(rootPath);

// Resolve to canonical final path (follows symbolic links/reparse points)
if (filePath.TryGetCanonicalPath(out var canonicalPath))
{
    Console.WriteLine(canonicalPath);
}

// FullPath is implicitly converted to string, so it works well with File/Directory methods
System.IO.File.WriteAllText(filePath, content);
````

## Analyzer rules

<!-- analyzer-rules -->
| Id | Category | Description | Severity | Enabled |
| -- | -- | -- | :--: | :--: |
| `MFFP0001` | FullPath | Path.GetFullPath is redundant on FullPath | Info | ✔️ |
| `MFFP0002` | FullPath | Combining with a FullPath right operand is redundant | Info | ✔️ |
| `MFFP0003` | FullPath | Path.GetFullPath with FullPath base is redundant | Info | ✔️ |
| `MFFP0004` | FullPath | Path.Combine is redundant with FullPath arguments | Info | ✔️ |
| `MFFP0005` | FullPath | Path.GetFileName is redundant on FullPath | Info | ✔️ |
| `MFFP0006` | FullPath | Path.GetFileNameWithoutExtension is redundant on FullPath | Info | ✔️ |
| `MFFP0007` | FullPath | Path.GetExtension is redundant on FullPath | Info | ✔️ |
| `MFFP0008` | FullPath | Path.GetDirectoryName is redundant on FullPath | Info | ✔️ |
| `MFFP0009` | FullPath | Path.ChangeExtension is redundant on FullPath | Info | ✔️ |
| `MFFP0010` | FullPath | Path.GetRelativePath is redundant on FullPath | Info | ✔️ |
| `MFFP0011` | FullPath | Method should return FullPath | Info | ✔️ |
<!-- analyzer-rules -->

# Additional resources

- [Simplifying paths handling in .NET code with the FullPath type](https://www.meziantou.net/simplifying-path-manipulations-with-the-fullpath-type.htm)
