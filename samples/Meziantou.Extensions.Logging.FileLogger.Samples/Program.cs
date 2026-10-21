using Meziantou.Extensions.Logging.FileLogger.Samples;

// The examples of the readme (src/Meziantou.Extensions.Logging.FileLogger/readme.md) and of the XML documentation are
// compiled in CI to keep them in sync with the API. They write the logs to the "logs" folder of the current directory.
// Run one with: dotnet run --project samples/Meziantou.Extensions.Logging.FileLogger.Samples -- <example>
switch (args)
{
    case ["host", .. var hostArgs]:
        await Examples.AddFileToHost(hostArgs);
        return 0;

    case ["provider"]:
        Examples.CreateProvider();
        return 0;

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
        Console.Error.WriteLine("Usage: host [args...] | provider | options | configuration | json");
        return 1;
}
