// See https://aka.ms/new-console-template for more information

Console.WriteLine("Clearing all recent documents");
Meziantou.Framework.Win32.RecentDocuments.ClearRecentDocuments();

var path = Path.Combine(Path.GetTempPath(), "Meziantou.Framework.Win32.RecentDocuments.sample.txt");
File.WriteAllText(path, "Sample document");

Console.WriteLine($"Adding '{path}' to the recent documents");
Meziantou.Framework.Win32.RecentDocuments.AddToRecentDocuments(path);
