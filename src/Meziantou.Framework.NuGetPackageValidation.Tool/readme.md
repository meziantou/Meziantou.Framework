# Meziantou.Framework.NuGetPackageValidation.Tool

`Meziantou.Framework.NuGetPackageValidation.Tool` is a tool to validate local `nupkg` file before pushing them to a server such as nuget.org.
It ensures the NuGet package follows good practices.

# How to use it

1. Install the tool

    ````bash
    dotnet tool update Meziantou.Framework.NuGetPackageValidation.Tool --global
    ````

2. Run the tool

    ````bash
    meziantou.validate-nuget-package "example.nupkg"
    ````

    If the package is not valid, the program exit with a non-zero value. All errors are written to the standard output in a JSON format.

You can show available options using:

````bash
meziantou.validate-nuget-package --help
````
