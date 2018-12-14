[![Build Status](https://meziantou.visualstudio.com/GitHub%20projects/_apis/build/status/meziantou.Meziantou.Framework?branchName=master)](https://meziantou.visualstudio.com/GitHub%20projects/_build/latest?definitionId=41?branchName=master)
[![GitHub license](https://img.shields.io/github/license/meziantou/Meziantou.Framework.svg)](https://github.com/meziantou/Meziantou.Framework/blob/master/LICENSE)

# NuGet packages

| Name | Version |
| :--- | :---: | 
| Meziantou.Framework | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.svg)](https://www.nuget.org/packages/Meziantou.Framework/) |
| Meziantou.Framework.CodeDom | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.CodeDom.svg)](https://www.nuget.org/packages/Meziantou.Framework.CodeDom/) |
| Meziantou.Framework.CommandLine | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.CommandLine.svg)](https://www.nuget.org/packages/Meziantou.Framework.CommandLine/) |
| Meziantou.Framework.Csv | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Csv.svg)](https://www.nuget.org/packages/Meziantou.Framework.Csv/) |
| Meziantou.Framework.Html | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Html.svg)](https://www.nuget.org/packages/Meziantou.Framework.Html/) |
| Meziantou.Framework.RelativeDate | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.RelativeDate.svg)](https://www.nuget.org/packages/Meziantou.Framework.RelativeDate/) |
| Meziantou.Framework.Scheduling | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Scheduling.svg)](https://www.nuget.org/packages/Meziantou.Framework.Scheduling/) |
| Meziantou.Framework.Templating | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Templating.svg)](https://www.nuget.org/packages/Meziantou.Framework.Templating/) |
| Meziantou.Framework.Templating.Html | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Templating.Html.svg)](https://www.nuget.org/packages/Meziantou.Framework.Templating.Html/) |
| Meziantou.Framework.TypeConverter | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.TypeConverter.svg)](https://www.nuget.org/packages/Meziantou.Framework.TypeConverter/) |
| Meziantou.Framework.Versioning | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Versioning.svg)](https://www.nuget.org/packages/Meziantou.Framework.Versioning/) |
| Meziantou.Framework.Win32.AccessToken | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Win32.AccessToken.svg)](https://www.nuget.org/packages/Meziantou.Framework.Win32.AccessToken/) |
| Meziantou.Framework.Win32.Amsi | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Win32.Amsi.svg)](https://www.nuget.org/packages/Meziantou.Framework.Win32.Amsi/) |
| Meziantou.Framework.Win32.ChangeJournal | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Win32.ChangeJournal.svg)](https://www.nuget.org/packages/Meziantou.Framework.Win32.ChangeJournal/) |
| Meziantou.Framework.Win32.CredentialManager | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Win32.CredentialManager.svg)](https://www.nuget.org/packages/Meziantou.Framework.Win32.CredentialManager/) |
| Meziantou.Framework.Win32.Dialogs | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Win32.Dialogs.svg)](https://www.nuget.org/packages/Meziantou.Framework.Win32.Dialogs/) |
| Meziantou.Framework.Win32.Jobs | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Win32.Jobs.svg)](https://www.nuget.org/packages/Meziantou.Framework.Win32.Jobs/) |
| Meziantou.Framework.Win32.Lsa | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Win32.Lsa.svg)](https://www.nuget.org/packages/Meziantou.Framework.Win32.Lsa/) |
| Meziantou.Framework.Win32.PerceivedType | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Win32.PerceivedType.svg)](https://www.nuget.org/packages/Meziantou.Framework.Win32.PerceivedType/) |
| Meziantou.Framework.Win32.RestartManager | [![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.Win32.RestartManager.svg)](https://www.nuget.org/packages/Meziantou.Framework.Win32.RestartManager/) |

# How to contribute

If you want to contribute to this repo, please [read the contributing guide](CONTRIBUTING.md) first.

How to setup your development environment:

1. Install the latest version of Visual Studio
2. Install the latest version of .NET SDK
3. Use the solution `Meziantou.Framework.sln`
4. You can run unit tests using the Test explorer in Visual Studio or the command line `dotnet test`

You can also use Visual Studio Code but you won't be able to run the WPF samples.

# Documentation

## Meziantou.Framework

Lots of extensions methods and utilities

````csharp
// IO Extensions
IOUtilities.PathCreateDirectory(@"c:\test\file.txt")
IOUtilities.MakeRelativePath(root: @"c:\test", path: @"c:\temp\file.txt") // ..\temp\file.txt
IOUtilities.ToValidFileName("tes/t.txt") // tes_x47_t.txt

// String Extensions
"test".EqualsIgnoreCase("Test")
"test".ContainsIgoreCase("ES")
"Tést".RemoveDiacritics() // Test

// Throttle / Debounce
action.Throttle(TimeSpan.FromMilliseconds(300));
action.Debounce(TimeSpan.FromMilliseconds(300));

// Slugs
Slug.Create("My super blog post") // my-super-blog-post

// And many more extensions/utilities
````

## Meziantou.Framework.TypeConverter

A universal converter that supports lots of conversion.

````csharp
ConvertUtilities.ChangeType("42", defaultValue: 0)
ConvertUtilities.ChangeType("Value1, 2", defaultValue: MyEnum.Unknown)
````

## Meziantou.Framework.Csv

CSV reader and writer.

````csharp
var reader = new CsvReader(textReader);
reader.HasHeaderRow = true;

CsvRow row;
while((row = (await reader.ReadRowAsync())) != null)
{
    row[0];        // Get value by index
    row["column1"] // Get value by column name
}
````

````csharp
var writer = new CsvWriter(sw);
await writer.WriteRowAsync("A", "B");
await writer.BeginRowAsync();
await writer.WriteValueAsync("C");
await writer.WriteValueAsync("D");
````

## Meziantou.Framework.Scheduling

Recurrence Rule parser, and ICS generator

````csharp
var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
var occurrences = rrule.GetNextOccurrences(startDate);
// 1997-09-02 09:00
// 1997-09-03 09:00
// 1997-09-04 09:00
````

## Meziantou.Framework.Win32.PerceivedType

Get the perceived type of a file: Text, Audio, Video, Document, Application, etc. 

````csharp
var perceived = Perceived.GetPerceivedType(".avi");
Assert.AreEqual(PerceivedType.Video, perceived.PerceivedType);
````

## Meziantou.Framework.Win32.CredentialManager

````csharp
CredentialManager.WriteCredential("ApplicationName", "username", "Pa$$w0rd", CredentialPersistence.Session);

var cred = CredentialManager.ReadCredential("ApplicationName");
Assert.AreEqual("username", cred.UserName);
Assert.AreEqual("Pa$$w0rd", cred.Password);

CredentialManager.DeleteCredential("ApplicationName");
````

## Meziantou.Framework.Templating

````csharp
Template template = new Template();
template.Load("Hello <%=Name%>!");
template.AddArgument("Name", typeof(string));

string result = template.Run("Meziantou"); // result= "Hello Meziantou!"
````

## Meziantou.Framework.Templating.Html

Extensions for Templating to support the html format: Encoding text, url or attribute. For email, it extracts the list of cid.

````csharp
var template = new HtmlEmailTemplate();
template.Load("<head><title>{{@begin section title}}Hello {{# Name }}{{@end section}}!</title></head>" +
"<body>{{#html "Here's an image"}} <img src=\"{{cid sample.png}}\" /></body>");
template.AddArgument("Name", typeof(string));

string result = template.Run("Meziantou", out var metadata);

Assert.AreEqual("<head><title>Hello Meziantou!</title></head><body>Here's an image <img src=\"cid:sample.png\"/></body>", result);
Assert.AreEqual("Hello Meziantou", metadata.Title);
Assert.AreEqual("sample.png", metadata.ContentIdentifiers[0]);
````
