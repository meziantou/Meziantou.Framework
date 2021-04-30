# Meziantou.Framework.TemporaryDirectory

Create a unique empty folder that is deleted at the end of the scope.

````c#
using var temporaryDirectory = TemporaryDirectory.Create();
temporaryDirectory.CreateEmptyFile("test/demo.txt");
File.WriteAllText(temporaryDirectory.GetFullPath("foo.txt"), "bar");
````