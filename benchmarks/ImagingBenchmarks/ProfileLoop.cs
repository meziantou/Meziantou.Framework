using System.Diagnostics;
using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The <c>profile</c> command: runs one benchmark method in a loop for a fixed duration, without BenchmarkDotNet, so that a
/// profiler (for example <c>dotnet-trace collect -- dotnet ImagingBenchmarks.dll profile
/// PngBenchmarks.SaveLarge8 20</c>) sees only the workload. Usage: <c>profile &lt;Class.Method&gt; [seconds]</c>.
/// </summary>
internal static class ProfileLoop
{
    public static void Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: profile <Class.Method> [seconds]");
            return;
        }

        var name = args[0].Split('.');
        var type = typeof(ProfileLoop).Assembly.GetTypes().Single(t => t.Name == name[0]);
        var method = type.GetMethod(name[1], BindingFlags.Public | BindingFlags.Instance) ?? throw new ArgumentException($"Unknown method {args[0]}.", nameof(args));
        var duration = TimeSpan.FromSeconds(args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 10);
        var instance = Activator.CreateInstance(type);
        var setup = type.GetMethods().SingleOrDefault(m => m.GetCustomAttribute<GlobalSetupAttribute>() is not null);
        var cleanup = type.GetMethods().SingleOrDefault(m => m.GetCustomAttribute<GlobalCleanupAttribute>() is not null);
        setup?.Invoke(instance, null);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var iterations = 0;
            while (stopwatch.Elapsed < duration)
            {
                method.Invoke(instance, null);
                iterations++;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{args[0]}: {iterations} iterations, {stopwatch.Elapsed.TotalMilliseconds / iterations:0.000} ms/op"));
        }
        finally
        {
            cleanup?.Invoke(instance, null);
            (instance as IDisposable)?.Dispose();
        }
    }
}
