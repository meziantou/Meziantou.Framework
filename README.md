# Meziantou.Framework

Lots of extensions methods and utilities

````csharp
IOUtilities.PathCreateDirectory(@"c:\test\file.txt")
IOUtilities.MakeRelativePath(root: @"c:\test", path: @"c:\temp\file.txt") // ..\temp\file.txt
IOUtilities.ToValidFileName("tes/t.txt") // tes_x47_t.txt

Slug.Create("My super blog post") // my-super-blog-post

"test".EqualsIgnoreCase("Test")
"test".ContainsIgoreCase("ES")
"Tést".RemoveDiacritics() // Test

// And many more extensions/utilities
````

# Meziantou.Framework.TypeConverter

A universal converter that supports lots of conversion.

````csharp
ConvertUtilities.ChangeType("42", defaultValue: 0)
````

# Meziantou.Framework.Csv

````csharp
var reader = new CsvReader(sr);
reader.HasHeaderRow = true;

CsvRow row;
while((row = reader.ReadRow()) != null)
{
    row1.GetValue(0);        // Get value by index
    row1.GetValue("column1") // Get value by column name
}
````

# Meziantou.Framework.Scheduling

Recurrence Rule parser, and ICS generator

````csharp
var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
var occurrences = rrule.GetNextOccurrences(startDate);
// 1997-09-02 09:00
// 1997-09-03 09:00
// 1997-09-04 09:00
````

# Meziantou.Framework.Win32.PerceivedType

Get the perceived type of a file: Text, Audio, Video, Document, Application, etc. 

````csharp
var perceived = Perceived.GetPerceivedType(".avi");
Assert.AreEqual(PerceivedType.Video, perceived.PerceivedType);
````

# Meziantou.Framework.Win32.CredentialManager

````csharp
CredentialManager.WriteCredential("ApplicationName", "username", "Pa$$w0rd", CredentialPersistence.Session);

var cred = CredentialManager.ReadCredential("ApplicationName");
Assert.AreEqual("username", cred.UserName);
Assert.AreEqual("Pa$$w0rd", cred.Password);

CredentialManager.DeleteCredential("ApplicationName");
````

# Meziantou.Framework.Templating

````csharp
Template template = new Template();
template.Load("Hello <%=Name%>!");
template.AddArgument("Name", typeof(string));

string result = template.Run("Meziantou"); // result= "Hello Meziantou!"
````

# Meziantou.Framework.Templating.Html

````csharp
var template = new HtmlEmailTemplate();
template.Load("Hello {{@begin section title}}{{# \"Meziantou\" }}{{@end section}}!");
HtmlEmailMetadata metadata;

// Act 
string result = template.Run(out metadata);

// Assert
Assert.AreEqual("Hello Meziantou!", result);
Assert.AreEqual("Meziantou", metadata.Title);
````