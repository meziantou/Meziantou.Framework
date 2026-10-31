---
title: Perceived types
description: Classify a file as text, image, audio, video, compressed, document or application from its extension, the way Windows does.
---

Windows classifies file extensions into broad kinds, the perceived types: text, image, audio, video and so on. File
Explorer uses them, for instance, to choose the columns of a folder of pictures. [Perceived](cref:Meziantou.Framework.Win32.Perceived) from the
`Meziantou.Framework.Win32.PerceivedType` package returns the perceived type of a file name or an extension.

<InstallNuGetPackage Id="Meziantou.Framework.Win32.PerceivedType" />

## Get the perceived type of a file

Pass a file name or an extension:

<!-- ref:../../tests/Meziantou.Framework.Win32.PerceivedType.Tests/PerceivedTests.cs;region=Perceived_GetPerceivedTypes;format=md-fenced-code-block;dedent=true -->
```csharp
// Get perceived type for a file extension
var perceived = Perceived.GetPerceivedType(".txt");
Assert.Equal(PerceivedType.Text, perceived.PerceivedType);
Assert.NotEqual(PerceivedTypeSource.Undefined, perceived.PerceivedTypeSource); // e.g. SoftCoded, NativeSupport

// Works with various file types
var image = Perceived.GetPerceivedType(".jpg");
Assert.Equal(PerceivedType.Image, image.PerceivedType);

var video = Perceived.GetPerceivedType(".mp4");
Assert.Equal(PerceivedType.Video, video.PerceivedType);

var audio = Perceived.GetPerceivedType(".mp3");
Assert.Equal(PerceivedType.Audio, audio.PerceivedType);
```
<!-- endref -->

The result gives the [Extension](cref:Meziantou.Framework.Win32.Perceived.Extension), the [PerceivedType](cref:Meziantou.Framework.Win32.PerceivedType), and the [PerceivedTypeSource](cref:Meziantou.Framework.Win32.PerceivedTypeSource), which tells how Windows knows the
type:

| [PerceivedTypeSource](cref:Meziantou.Framework.Win32.PerceivedTypeSource) | The type comes from |
| --- | --- |
| [SoftCoded](cref:Meziantou.Framework.Win32.PerceivedTypeSource.SoftCoded) | A file association in the registry |
| [HardCoded](cref:Meziantou.Framework.Win32.PerceivedTypeSource.HardCoded) | The operating system, or [AddPerceived](cref:Meziantou.Framework.Win32.Perceived.AddPerceived) |
| [NativeSupport](cref:Meziantou.Framework.Win32.PerceivedTypeSource.NativeSupport) | A codec provided with the operating system |
| [GdiPlus](cref:Meziantou.Framework.Win32.PerceivedTypeSource.GdiPlus) | The image formats of GDI+ |
| [WmSdk](cref:Meziantou.Framework.Win32.PerceivedTypeSource.WmSdk) | The formats of Windows Media |
| [ZipFolder](cref:Meziantou.Framework.Win32.PerceivedTypeSource.ZipFolder) | The compressed folders of Windows |
| [Undefined](cref:Meziantou.Framework.Win32.PerceivedTypeSource.Undefined) | Nowhere: the extension has no perceived type |

The types are [Text](cref:Meziantou.Framework.Win32.PerceivedType.Text), [Image](cref:Meziantou.Framework.Win32.PerceivedType.Image), [Audio](cref:Meziantou.Framework.Win32.PerceivedType.Audio), [Video](cref:Meziantou.Framework.Win32.PerceivedType.Video), [Compressed](cref:Meziantou.Framework.Win32.PerceivedType.Compressed), [Document](cref:Meziantou.Framework.Win32.PerceivedType.Document), [System](cref:Meziantou.Framework.Win32.PerceivedType.System), [Application](cref:Meziantou.Framework.Win32.PerceivedType.Application), [GameMedia](cref:Meziantou.Framework.Win32.PerceivedType.GameMedia) and
[Contacts](cref:Meziantou.Framework.Win32.PerceivedType.Contacts). An extension that Windows does not know is [Unspecified](cref:Meziantou.Framework.Win32.PerceivedType.Unspecified), and [Custom](cref:Meziantou.Framework.Win32.PerceivedType.Custom) is a type defined in the registry
that is not one of the others.

The answer depends on the machine: Windows and the installed applications register the file associations, so the
same extension can have a different type, or none, on another machine. An extension with no registered association is
[Unspecified](cref:Meziantou.Framework.Win32.PerceivedType.Unspecified).

## Define the perceived type of an extension

Define the types your application relies on, instead of depending on the applications installed on the machine:

<!-- ref:../../tests/Meziantou.Framework.Win32.PerceivedType.Tests/PerceivedTests.cs;region=Perceived_AddPerceived;format=md-fenced-code-block;dedent=true -->
```csharp
// Add a custom perceived type
Perceived.AddPerceived(".myext", PerceivedType.Text);
Assert.Equal(PerceivedType.Text, Perceived.GetPerceivedType("file.myext").PerceivedType);

// Add default perceived types for common extensions
Perceived.AddDefaultPerceivedTypes();
```
<!-- endref -->

- [AddPerceived](cref:Meziantou.Framework.Win32.Perceived.AddPerceived) replaces the type of an extension for the current process only: it does not change the registry.
- [AddDefaultPerceivedTypes](cref:Meziantou.Framework.Win32.Perceived.AddDefaultPerceivedTypes) defines common development extensions: `.cs`, `.csproj`, `.sln`, `.xaml`, `.html`,
  `.js`, `.css`, `.sql` and others as [Text](cref:Meziantou.Framework.Win32.PerceivedType.Text), and `.exe`, `.dll` and `.class` as [Application](cref:Meziantou.Framework.Win32.PerceivedType.Application).
- The extensions are compared without regard to case.

## Caching

[GetPerceivedType](cref:Meziantou.Framework.Win32.Perceived.GetPerceivedType) remembers the answers of Windows for the lifetime of the process, so a change of the file
associations is not seen until the application restarts. The cache is bounded: when the names come from untrusted
input, such as uploaded files, unusual extensions do not make it grow without limit.
