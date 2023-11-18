// See https://aka.ms/new-console-template for more information

Console.WriteLine("Clearing all recent documents");
Meziantou.Framework.Win32.RecentDocuments.ClearRecentDocuments();

Console.WriteLine("Add calc to recent document");
Meziantou.Framework.Win32.RecentDocuments.AddToRecentDocuments("C:\\Users\\mezia\\source\\repos\\FileEtw\\FileEtw.sln");
