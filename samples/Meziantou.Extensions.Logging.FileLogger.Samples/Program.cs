using Meziantou.Extensions.Logging.FileLogger.Samples;

// The examples are compiled in CI to keep the XML documentation in sync with the API.
// They write the logs to the "logs" folder of the current directory.
if (args is ["host", .. var hostArgs])
{
    await Examples.AddFileToHost(hostArgs);
    return 0;
}

if (args is ["provider"])
{
    Examples.CreateProvider();
    return 0;
}

Console.WriteLine("Usage: host [args...] | provider");
return 1;
