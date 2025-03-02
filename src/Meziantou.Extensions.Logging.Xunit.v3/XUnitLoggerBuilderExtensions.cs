using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable IDE1006 // Naming Styles
namespace Meziantou.Extensions.Logging.Xunit.v3;
#pragma warning restore IDE1006 // Naming Styles

public static class XUnitLoggerBuilderExtensions
{
    public static ILoggingBuilder AddXunit(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ILoggerProvider, XUnitLoggerProvider>();
        return builder;
    }

    public static ILoggingBuilder AddXunit(this ILoggingBuilder builder, ITestOutputHelper? testOutputHelper)
    {
        builder.Services.AddSingleton<ILoggerProvider>(_ => new XUnitLoggerProvider(testOutputHelper));
        return builder;
    }

    public static ILoggingBuilder AddXunit(this ILoggingBuilder builder, XUnitLoggerOptions? options)
    {
        builder.Services.AddSingleton<ILoggerProvider>(_ => new XUnitLoggerProvider(options));
        return builder;
    }

    public static ILoggingBuilder AddXunit(this ILoggingBuilder builder, ITestOutputHelper? testOutputHelper, XUnitLoggerOptions? options)
    {
        builder.Services.AddSingleton<ILoggerProvider>(_ => new XUnitLoggerProvider(testOutputHelper, options));
        return builder;
    }
}
