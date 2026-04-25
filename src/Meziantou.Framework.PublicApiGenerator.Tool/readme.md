# Meziantou.Framework.PublicApiGenerator.Tool

CLI tool to generate compilable C# public API stubs from a .NET `.dll` or `.exe`.

<!-- help -->
```
Description:
  Generate public API stubs from a .NET assembly.

Usage:
  Meziantou.Framework.PublicApiGenerator.Tool [options]

Options:
  --input <input> (REQUIRED)                       Path to a .NET dll or exe file
  --output <output> (REQUIRED)                     Output directory
  --file-layout                                    File layout: SingleFile, OneFilePerNamespace, or OneFilePerType 
  <OneFilePerNamespace|OneFilePerType|SingleFile>  [default: SingleFile]
  -?, -h, --help                                   Show help and usage information
  --version                                        Show version information
```
<!-- help -->
