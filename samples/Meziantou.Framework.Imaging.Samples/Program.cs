using Meziantou.Framework.Imaging.Samples;

// The examples are compiled in CI to keep the documentation in sync with the API, and run by the unit tests
// (tests/Meziantou.Framework.Imaging.Tests/Documentation). Run one with:
// dotnet run --project samples/Meziantou.Framework.Imaging.Samples -- <example> <args...>
if (args is ["resize", var input, var output])
{
    Examples.ResizeAnimation(input, output);
    return 0;
}

if (args is ["animation", var apng, var gif])
{
    Examples.BuildAnimation(apng, gif);
    return 0;
}

// Package README quick starts: read animation.gif and write their outputs in the current directory
if (args is ["readme"])
{
    ReadmeSnippets.QuickStart();
    return 0;
}

if (args is ["package-readme"])
{
    PackageReadmeSnippets.QuickStart();
    return 0;
}

Console.WriteLine("Usage: resize <input> <output> | animation <output.apng> <output.gif> | readme | package-readme");
return 1;
