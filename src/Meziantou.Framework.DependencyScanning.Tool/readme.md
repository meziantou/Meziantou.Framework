# Meziantou.Framework.DependencyScanning.Tool

`Meziantou.Framework.DependencyScanning.Tool` is a .NET tool to update dependencies detected by `Meziantou.Framework.DependencyScanning`.

# How to use it

1. Install the tool

    ````bash
    dotnet tool update Meziantou.Framework.DependencyScanning.Tool --global
    ````

2. Run the tool

    ````bash
    Meziantou.Framework.DependencyScanning.Tool update --directory .
    ````

You can show available options using:

````bash
Meziantou.Framework.DependencyScanning.Tool --help
````

<!-- help -->
```
Description:
  List and update dependencies detected in a folder.

Usage:
  Meziantou.Framework.DependencyScanning.Tool [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  update  Update dependencies
  list    List dependencies
```
<!-- help -->