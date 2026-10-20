using Meziantou.Framework.Globbing.Samples;

// The examples are shown by the XML documentation and compiled in CI to keep them in sync with the API.
// Run one with: dotnet run --project samples/Meziantou.Framework.Globbing.Samples -- <glob|collection>
switch (args)
{
    case ["glob"]:
        Examples.EnumerateFiles();
        return 0;

    case ["collection"]:
        Examples.EnumerateFilesWithGlobCollection();
        return 0;

    default:
        Console.Error.WriteLine("Usage: <glob|collection>");
        return 1;
}
