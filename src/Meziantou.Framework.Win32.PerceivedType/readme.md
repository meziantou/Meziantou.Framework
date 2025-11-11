# Meziantou.Framework.Win32.PerceivedType

A .NET library for determining a file's perceived type based on its extension using Windows APIs.

## Usage

This library provides functionality to get a file's perceived type (text, image, audio, video, etc.) based on its extension by querying the Windows registry and using the `AssocGetPerceivedType` Win32 API.

### Get Perceived Type

```csharp
using Meziantou.Framework.Win32;

// Get perceived type for a file extension
var perceived = Perceived.GetPerceivedType(".txt");
Console.WriteLine(perceived.PerceivedType); // Text
Console.WriteLine(perceived.PerceivedTypeSource); // SoftCoded or HardCoded

// Works with various file types
var image = Perceived.GetPerceivedType(".jpg");
Console.WriteLine(image.PerceivedType); // Image

var video = Perceived.GetPerceivedType(".mp4");
Console.WriteLine(video.PerceivedType); // Video

var audio = Perceived.GetPerceivedType(".mp3");
Console.WriteLine(audio.PerceivedType); // Audio
```

### Add Custom Perceived Types

You can add custom perceived types or override system-defined types:

```csharp
// Add a custom perceived type
Perceived.AddPerceived(".myext", PerceivedType.Text);

// Add default perceived types for common extensions
Perceived.AddDefaultPerceivedTypes();
```

### Perceived Types

The library supports the following perceived types:

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
- `Mime` - Determined through MIME content types

## Platform Support

This library is **Windows-only** and requires Windows XP or later (Windows 5.1.2600+).

## Additional Resources

- [AssocGetPerceivedType function (Windows)](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-assocgetperceivedtype?WT.mc_id=DT-MVP-5003978)
