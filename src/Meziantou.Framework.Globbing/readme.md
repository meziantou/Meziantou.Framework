# Meziantou.Framework.Globbing

## Supported glob features

- `*` matches any number of characters including none
- `?` matches a single character
- `[abc]` matches one character given in the bracket
- `[!abc]` matches any character not in the brackets
- `[a-z]` matches one character from the range given in the bracket
- `[!a-z]` matches one character not in the range given in the bracket
- `{abc,123}` matches one of the literals
- `**` matches zero or more directories

## Usage

Install the NuGet package `Meziantou.Framework.Globbing` ([NuGet](https://www.nuget.org/packages/Meziantou.Framework.Globbing/))

````xml
<PackageReference Include="Meziantou.Framework.Globbing" Version="1.0.4" />
````

- `IsMatch` tests whether a file matches the glob pattern

    ````csharp
    Glob glob = Glob.Parse("src/**/*.txt", GlobOptions.IgnoreCase);
    glob.IsMatch("src/abc.txt");
    ````

- Enumerate files that match a glob pattern

    ````csharp
    // Enumerate files that match the glob in the folder rootDirectory
    Glob glob = Glob.Parse("src/**/*.txt", GlobOptions.None);
    foreach(var file in glob.EnumerateFiles("rootDirectory"))
    {
        Console.WriteLine(file);
    }
    ````

## Addition resources

- [Enumerating files using Globbing and System.IO.Enumeration](https://www.meziantou.net/enumerating-files-using-globbing-and-system-io-enumeration.htm)