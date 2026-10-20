using Meziantou.Framework.TemporaryContainers.Samples;

// The examples are compiled in CI to keep the documentation in sync with the API. They need a container runtime. Run one with:
// dotnet run --project samples/Meziantou.Framework.TemporaryContainers.Samples -- <example>
if (args is ["container"])
{
    await Examples.CreateContainer();
    return 0;
}

if (args is ["volume"])
{
    await Examples.CreateContainerWithVolume();
    return 0;
}

Console.WriteLine("Usage: container | volume");
return 1;
