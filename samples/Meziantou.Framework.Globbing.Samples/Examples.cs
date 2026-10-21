namespace Meziantou.Framework.Globbing.Samples;

internal static class Examples
{
    public static void EnumerateFiles()
    {
        #region Glob_EnumerateFiles
        var glob = Glob.Parse("**/*.cs", GlobDialect.Standard, GlobOptions.IgnoreCase);
        foreach (var file in glob.EnumerateFiles("C:/MyProject"))
        {
            Console.WriteLine(file);
        }
        #endregion
    }

    public static void EnumerateFilesWithGlobCollection()
    {
        #region GlobCollection_EnumerateFiles
        var globs = new GlobCollection(
            Glob.Parse("**/*.txt", GlobDialect.Standard),
            Glob.Parse("!temp/**/*", GlobDialect.Standard));

        foreach (var file in globs.EnumerateFiles("C:/MyProject"))
        {
            Console.WriteLine(file);
        }
        #endregion
    }
}
