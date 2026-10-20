# Meziantou.Framework.Win32.PerceivedType

A .NET library for determining a file's perceived type based on its extension using Windows APIs.

## Usage

This library provides functionality to get a file's perceived type (text, image, audio, video, etc.) based on its extension by querying the Windows registry and using the `AssocGetPerceivedType` Win32 API.

The types are in the `Meziantou.Framework.Win32` namespace.

### Get Perceived Type

<!-- ref:../../tests/Meziantou.Framework.Win32.PerceivedType.Tests/PerceivedTests.cs;region=Perceived_GetPerceivedType;format=md-fenced-code-block;dedent=true -->
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

### Add Custom Perceived Types

You can add custom perceived types or override system-defined types. They are stored in memory, for the current process:

<!-- ref:../../tests/Meziantou.Framework.Win32.PerceivedType.Tests/PerceivedTests.cs;region=Perceived_AddPerceived;format=md-fenced-code-block;dedent=true -->
```csharp
// Add a custom perceived type
Perceived.AddPerceived(".myext", PerceivedType.Text);
Assert.Equal(PerceivedType.Text, Perceived.GetPerceivedType("file.myext").PerceivedType);

// Add default perceived types for common extensions
Perceived.AddDefaultPerceivedTypes();
```
<!-- endref -->

### Perceived Types

The library supports the following perceived types. Which extension maps to which type is
decided by Windows and by the file associations registered on the machine, so the examples
below are illustrative rather than guaranteed: an extension with no registered handler
reports `Unspecified`.

- `Text` - Text files (.txt, .cs, .html, .xml, etc.)
- `Image` - Image files (.jpg, .png, .gif, etc.)
- `Audio` - Audio files (.mp3, .wav, etc.)
- `Video` - Video files (.mp4, .avi, .mpeg, etc.)
- `Compressed` - Compressed files (.zip, .rar, etc.)
- `Document` - Document files (.doc, .pdf, etc.)
- `Application` - Application files (.exe, .dll, etc.)
- `System` - System files
- `GameMedia` - Game media files
- `Contacts` - Contact files
- `Custom` - Custom type defined in registry
- `Unspecified` - No perceived type
- `Unknown` - Unknown type

### Perceived Type Source

The `PerceivedTypeSource` enum indicates where the perceived type information comes from:

- `Undefined` - No perceived type was found
- `SoftCoded` - Determined through registry association
- `HardCoded` - Inherently known to the OS
- `NativeSupport` - Determined through OS codec
- `GdiPlus` - Supported by GDI+
- `WmSdk` - Supported by Windows Media SDK
- `ZipFolder` - Supported by Windows compressed folders

## Platform Support

This library is **Windows-only** and requires Windows XP or later (Windows 5.1.2600+).

## Additional Resources

- [AssocGetPerceivedType function (Windows)](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-assocgetperceivedtype?WT.mc_id=DT-MVP-5003978)
