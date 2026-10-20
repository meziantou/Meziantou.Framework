namespace Meziantou.Framework.Win32.RecentDocumentsConsole;

// Example of the readme (src/Meziantou.Framework.Win32.RecentDocuments/readme.md). It changes the recent documents of
// the user, so it is compiled in CI to keep the documentation in sync with the API, but not run.
public static class ReadmeExamples
{
    public static void Usage()
    {
        #region RecentDocuments_Usage
        // Add a document to Recent Items and to the Jump List of the current application.
        // A relative path is resolved against the current directory.
        Meziantou.Framework.Win32.RecentDocuments.AddToRecentDocuments(@"C:\Users\Public\Documents\file.txt");

        // Add a document to the Jump List of the application identified by an AppUserModelID (Windows 7+).
        // Use it when the application sets an explicit AppUserModelID, or runs through "dotnet app.dll".
        Meziantou.Framework.Win32.RecentDocuments.AddToRecentDocuments(@"C:\Users\Public\Documents\file.txt", "Company.Product.App");

        // Warning: clears Recent Items and the Recent/Frequent lists of every application's Jump List, not only yours.
        Meziantou.Framework.Win32.RecentDocuments.ClearRecentDocuments();
        #endregion
    }
}
