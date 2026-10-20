# Mark of the Web

The Mark of the Web (MOTW) is a security feature in Windows that helps protect users from potentially unsafe content downloaded from the internet. Windows records the security zone a file came from in an NTFS alternate data stream named `Zone.Identifier`, alongside the URLs it was downloaded from.
When a user attempts to open a file with MOTW, Windows may display a warning message or restrict certain actions, such as running scripts or accessing certain features, to help prevent potential security risks.

## Usage

To add the Mark of the Web to a file, you can use the `MarkOfTheWeb` class (in the `Meziantou.Framework.Win32` namespace) provided in this library. Here's an example of how to use it:

<!-- ref:../../tests/Meziantou.Framework.Win32.MarkOfTheWeb.Tests/MarkOfTheWebTests.cs;region=MarkOfTheWeb_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var path = Path.GetTempFileName();

// Add the Mark of the Web to a file
MarkOfTheWeb.SetFileZone(path, UrlZone.Internet, referrerUrl: "https://example.com/", hostUrl: "https://example.com/file.zip");

// Get the zone Windows assigns to a file
var zone = MarkOfTheWeb.GetFileZone(path);
Assert.Equal(UrlZone.Internet, zone);

// Check whether a file comes from the Internet or Restricted Sites zones
Assert.True(MarkOfTheWeb.IsUntrusted(path));

// Read what the Zone.Identifier stream records
var zoneIdentifier = MarkOfTheWeb.GetFileZoneIdentifier(path); // null when the file has no Mark of the Web
Assert.NotNull(zoneIdentifier);
Assert.Equal(UrlZone.Internet, zoneIdentifier.Zone);
Assert.Equal("https://example.com/file.zip", zoneIdentifier.HostUrl);
Assert.Equal("https://example.com/", zoneIdentifier.ReferrerUrl);

// Read the raw content of the Zone.Identifier stream
var content = MarkOfTheWeb.GetFileZoneContent(path);
Assert.Equal("""
    [ZoneTransfer]
    ZoneId=3
    ReferrerUrl=https://example.com/
    HostUrl=https://example.com/file.zip

    """, content, ignoreLineEndingDifferences: true);

// Remove the Mark of the Web from a file
MarkOfTheWeb.RemoveFileZone(path);
Assert.Null(MarkOfTheWeb.GetFileZoneIdentifier(path));
Assert.False(MarkOfTheWeb.IsUntrusted(path));
```
<!-- endref -->

Use `GetFileZone` or `IsUntrusted` to make a security decision. `GetFileZoneIdentifier` and `GetFileZoneContent` report what the stream contains, which is not always the zone Windows applies to the file.
