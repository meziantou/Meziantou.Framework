# Meziantou.Framework.TemporaryDirectory

## TemporaryDirectory

Create a unique empty folder that is deleted at the end of the scope.

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryDirectory_Create;format=md-fenced-code-block;dedent=true -->
```csharp
using var temporaryDirectory = TemporaryDirectory.Create();
temporaryDirectory.CreateEmptyFile("test/demo.txt");
File.WriteAllText(temporaryDirectory.GetFullPath("foo.txt"), "bar");
```
<!-- endref -->

## TemporaryFile

Create a unique file that is deleted at the end of the scope.

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryFile_Create;format=md-fenced-code-block;dedent=true -->
```csharp
// Generated name under the system temp folder
using var temporaryFile = TemporaryFile.Create();
File.WriteAllText(temporaryFile.FullPath, "content");

// Choose the file name; a unique parent folder is created for it
using var namedFile = TemporaryFile.Create("custom.txt");

// Choose the full path. The file must not already exist.
using var atPath = TemporaryFile.Create(FullPath.Combine(Path.GetTempPath(), $"custom-{Guid.NewGuid():N}.txt"));
```
<!-- endref -->
