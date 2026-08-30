# Meziantou.Framework.TemporaryDirectory

## TemporaryDirectory

Create a unique empty folder that is deleted at the end of the scope.

````c#
using var temporaryDirectory = TemporaryDirectory.Create();
temporaryDirectory.CreateEmptyFile("test/demo.txt");
File.WriteAllText(temporaryDirectory.GetFullPath("foo.txt"), "bar");
````

## TemporaryFile

Create a unique file that is deleted at the end of the scope.

````c#
// Generated name under the system temp folder
using var temporaryFile = TemporaryFile.Create();
File.WriteAllText(temporaryFile.FullPath, "content");

// Choose the file name; a unique parent folder is created for it
using var namedFile = TemporaryFile.Create("custom.txt");

// Choose the full path. The file must not already exist.
using var atPath = TemporaryFile.Create(FullPath.Combine(Path.GetTempPath(), "custom.txt"));
````
