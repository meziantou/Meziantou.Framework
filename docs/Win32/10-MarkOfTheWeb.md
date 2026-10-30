---
title: Mark of the Web
description: Tell whether a file was downloaded from the Internet, mark the files your application downloads, and read the Zone.Identifier stream.
---

Windows records where a downloaded file comes from in an NTFS alternate data stream named `Zone.Identifier`: the
security zone, and the URLs the file was downloaded from. This is the Mark of the Web. Windows uses it to warn the user
before opening the file, and to block macros and scripts from the Internet. [MarkOfTheWeb](cref:Meziantou.Framework.Win32.MarkOfTheWeb) from the
`Meziantou.Framework.Win32.MarkOfTheWeb` package reads and writes it.

```bash
dotnet add package Meziantou.Framework.Win32.MarkOfTheWeb
```

## Read and write the Mark of the Web

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
```
<!-- endref -->

| Method | Description |
| --- | --- |
| [SetFileZone](cref:Meziantou.Framework.Win32.MarkOfTheWeb.SetFileZone) | Writes the `Zone.Identifier` stream with the zone, and optionally the referrer URL and the host URL |
| [GetFileZone](cref:Meziantou.Framework.Win32.MarkOfTheWeb.GetFileZone) | Returns the zone Windows assigns to the file, or [UrlZone.Invalid](cref:Meziantou.Framework.Win32.UrlZone.Invalid) when it cannot be determined |
| [IsUntrusted](cref:Meziantou.Framework.Win32.MarkOfTheWeb.IsUntrusted) | Returns `true` when the file comes from the Internet or Restricted Sites zones, or when Windows cannot evaluate its zone |
| [GetFileZoneIdentifier](cref:Meziantou.Framework.Win32.MarkOfTheWeb.GetFileZoneIdentifier) | Returns the content of the stream, parsed, or `null` when the file has no Mark of the Web |
| [GetFileZoneContent](cref:Meziantou.Framework.Win32.MarkOfTheWeb.GetFileZoneContent) | Returns the raw content of the stream, or `null` when the file has no Mark of the Web |
| [RemoveFileZone](cref:Meziantou.Framework.Win32.MarkOfTheWeb.RemoveFileZone) | Deletes the stream. It does nothing when the file has no Mark of the Web |

## Make a security decision

Use [GetFileZone](cref:Meziantou.Framework.Win32.MarkOfTheWeb.GetFileZone) or [IsUntrusted](cref:Meziantou.Framework.Win32.MarkOfTheWeb.IsUntrusted) to decide whether to trust a file. They ask Windows for the zone of the file, which
takes the security settings of the machine into account. [GetFileZoneIdentifier](cref:Meziantou.Framework.Win32.MarkOfTheWeb.GetFileZoneIdentifier) and [GetFileZoneContent](cref:Meziantou.Framework.Win32.MarkOfTheWeb.GetFileZoneContent) report what
the stream records, which is not always the zone Windows applies.

The zones are:

| [UrlZone](cref:Meziantou.Framework.Win32.UrlZone) | Zone |
| --- | --- |
| [LocalMachine](cref:Meziantou.Framework.Win32.UrlZone.LocalMachine) | Files on the local computer |
| [Intranet](cref:Meziantou.Framework.Win32.UrlZone.Intranet) | Addresses on the local network |
| [Trusted](cref:Meziantou.Framework.Win32.UrlZone.Trusted) | Trusted Sites |
| [Internet](cref:Meziantou.Framework.Win32.UrlZone.Internet) | The Internet, for the addresses that belong to no other zone |
| [Untrusted](cref:Meziantou.Framework.Win32.UrlZone.Untrusted) | Restricted Sites |

Windows can also report a custom zone, numbered 1000 and above, that has no named member.

## Mark the files your application downloads

An application that downloads files, or extracts them from a downloaded archive, should mark them so Windows protects
the user the same way as for the files downloaded by a browser. Copy the Mark of the Web of the archive to the
extracted files:

<!-- ref:../../tests/Meziantou.Framework.Win32.MarkOfTheWeb.Tests/MarkOfTheWebTests.cs;region=MarkOfTheWeb_CopyZone;format=md-fenced-code-block;dedent=true -->
```csharp
// Give the files extracted from a downloaded archive the Mark of the Web of the archive
var zoneIdentifier = MarkOfTheWeb.GetFileZoneIdentifier(archivePath);
if (zoneIdentifier is { Zone: >= UrlZone.LocalMachine and <= UrlZone.Untrusted })
{
    MarkOfTheWeb.SetFileZone(extractedPath, zoneIdentifier.Zone, zoneIdentifier.ReferrerUrl, zoneIdentifier.HostUrl);
}
```
<!-- endref -->

- [SetFileZone](cref:Meziantou.Framework.Win32.MarkOfTheWeb.SetFileZone) only accepts the five zones of the table above: another value throws an `ArgumentOutOfRangeException`.
- The URLs cannot contain a line break or a null character (`ArgumentException`). An empty URL is written as no URL.
- [SetFileZone](cref:Meziantou.Framework.Win32.MarkOfTheWeb.SetFileZone) throws a `FileNotFoundException` when the file does not exist. A directory is not a file.
- A read-only file is marked too, and stays read-only.

The Mark of the Web is an NTFS feature: on file systems without alternate data streams, such as FAT32, files cannot
carry it.

## Parse the content of a stream

[ZoneIdentifier.Parse](cref:Meziantou.Framework.Win32.ZoneIdentifier.Parse) reads content in the format of the `Zone.Identifier` stream, for instance a stream copied from
another file or received from another machine:

<!-- ref:../../tests/Meziantou.Framework.Win32.MarkOfTheWeb.Tests/MarkOfTheWebTests.cs;region=ZoneIdentifier_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var zoneIdentifier = ZoneIdentifier.Parse("""
    [ZoneTransfer]
    ZoneId=3
    HostUrl=https://example.com/file.zip
    """);

Assert.Equal(UrlZone.Internet, zoneIdentifier.Zone);
Assert.Equal("https://example.com/file.zip", zoneIdentifier.HostUrl);
Assert.Null(zoneIdentifier.ReferrerUrl);
```
<!-- endref -->

The names of the section and the keys are case-insensitive. A key that is missing, empty, or repeated with different
values is reported as absent: `null` for the URLs, [UrlZone.Invalid](cref:Meziantou.Framework.Win32.UrlZone.Invalid) for the zone.
