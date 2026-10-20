using Meziantou.Extensions.Logging.FileLogger.Samples;

// The examples of the readme (src/Meziantou.Extensions.Logging.FileLogger/readme.md) configure the logging of a host.
// They are compiled in CI to keep the documentation in sync with the API. Run one with:
// dotnet run --project samples/Meziantou.Extensions.Logging.FileLogger.Samples -- <example>
switch (args)
{
    case ["options"]:
        ReadmeExamples.ConfigureWithOptions(args);
        return 0;

    case ["configuration"]:
        ReadmeExamples.ConfigureFromConfiguration(args);
        return 0;

    case ["json"]:
        ReadmeExamples.UseJsonFormatter(args);
        return 0;

    default:
        Console.Error.WriteLine("Usage: <options|configuration|json>");
        return 1;
}
