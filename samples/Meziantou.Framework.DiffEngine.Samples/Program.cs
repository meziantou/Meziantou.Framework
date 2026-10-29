// The examples of the documentation (docs/80-DiffEngine) are compiled in CI. They start a diff tool, so they do not
// run in tests.
using System.Diagnostics;
using Meziantou.Framework.DiffEngine;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Meziantou.Framework.DiffEngine.Samples <received file> <verified file>");
    return 1;
}

CompareFiles(args[0], args[1]);
return 0;

static void CompareFiles(string receivedFile, string verifiedFile)
{
    #region DiffTools_StartTool
    // A tool that supports this binary extension, or else the first tool that supports text
    if (!DiffTools.TryFindByExtension(Path.GetExtension(verifiedFile), out var tool))
    {
        Console.WriteLine("No diff tool found");
        return;
    }

    // The received file holds the new content; the verified file is the one to update
    var arguments = tool.GetArguments(receivedFile, verifiedFile);
    using var process = Process.Start(tool.ExePath, arguments);
    #endregion
}
