using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

// "profile <Class.Method> <count>" runs a benchmark method a fixed number of times, without BenchmarkDotNet, for a profiler
if (args is ["profile", var name, var countText])
{
    var separator = name.LastIndexOf('.', StringComparison.Ordinal);
    var type = typeof(Program).Assembly.GetTypes().Single(type => string.Equals(type.Name, name[..separator], StringComparison.Ordinal));
    var method = type.GetMethod(name[(separator + 1)..])!;
    var instance = Activator.CreateInstance(type);
    type.GetMethods().SingleOrDefault(method => method.GetCustomAttribute<GlobalSetupAttribute>() is not null)?.Invoke(instance, []);
    var count = int.Parse(countText, System.Globalization.CultureInfo.InvariantCulture);
    for (var i = 0; i < count; i++)
    {
        method.Invoke(instance, []);
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
