using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable IDE1006 // Naming Styles
namespace Meziantou.Extensions.Logging.Xunit.v3;
#pragma warning restore IDE1006 // Naming Styles

/// <summary>Provides extension methods for <see cref="ILoggingBuilder"/> to add xUnit.net logging support.</summary>
/// <example>
/// <code>
/// using Microsoft.Extensions.Hosting;
///
/// var host = new HostBuilder()
///     .ConfigureLogging(builder =>
///     {
///         builder.AddXunit(testOutputHelper);
///     })
///     .Build();
/// </code>
/// </example>
public static class XUnitLoggerBuilderExtensions
{
    /// <summary>Adds an xUnit.net logger provider to the logging builder.</summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the provider to.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> so that additional calls can be chained.</returns>
    public static ILoggingBuilder AddXunit(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ILoggerProvider, XUnitLoggerProvider>();
        return builder;
    }

    /// <summary>Adds an xUnit.net logger provider to the logging builder with the specified test output helper.</summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the provider to.</param>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> so that additional calls can be chained.</returns>
    public static ILoggingBuilder AddXunit(this ILoggingBuilder builder, ITestOutputHelper? testOutputHelper)
    {
        builder.Services.AddSingleton<ILoggerProvider>(_ => new XUnitLoggerProvider(testOutputHelper));
        return builder;
    }

    /// <summary>Adds an xUnit.net logger provider to the logging builder with the specified options.</summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the provider to.</param>
    /// <param name="options">The logger options.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> so that additional calls can be chained.</returns>
    public static ILoggingBuilder AddXunit(this ILoggingBuilder builder, XUnitLoggerOptions? options)
    {
        builder.Services.AddSingleton<ILoggerProvider>(_ => new XUnitLoggerProvider(options));
        return builder;
    }

    /// <summary>Adds an xUnit.net logger provider to the logging builder with the specified test output helper and options.</summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the provider to.</param>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <param name="options">The logger options.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> so that additional calls can be chained.</returns>
    public static ILoggingBuilder AddXunit(this ILoggingBuilder builder, ITestOutputHelper? testOutputHelper, XUnitLoggerOptions? options)
    {
        builder.Services.AddSingleton<ILoggerProvider>(_ => new XUnitLoggerProvider(testOutputHelper, options));
        return builder;
    }
}
