using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace Meziantou.Extensions.Logging;

/// <summary>Provides extension methods for <see cref="ILoggingBuilder"/> to add file logging support.</summary>
/// <example>
/// <code>
/// var builder = Host.CreateApplicationBuilder(args);
/// builder.Logging.AddFile(options =>
/// {
///     options.Directory = "logs";
///     options.RollInterval = RollInterval.Daily;
///     options.MaxRetainedFiles = 7;
/// });
/// </code>
/// </example>
public static class FileLoggerBuilderExtensions
{
    /// <summary>Adds a file logger provider to the logging builder. The options are read from the <c>Logging:File</c> configuration section.</summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the provider to.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> so that additional calls can be chained.</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(FileLoggerOptions))]
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access", Justification = "The members of FileLoggerOptions used by the configuration binder are preserved by the DynamicDependency attribute")]
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddConfiguration();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, FileLoggerProvider>(CreateProvider));
        LoggerProviderOptions.RegisterProviderOptions<FileLoggerOptions, FileLoggerProvider>(builder.Services);
        return builder;

        static FileLoggerProvider CreateProvider(IServiceProvider serviceProvider)
        {
            var options = serviceProvider.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>();
            return new FileLoggerProvider(options, serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System);
        }
    }

    /// <summary>Adds a file logger provider that writes to the specified directory.</summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the provider to.</param>
    /// <param name="logsDirectory">The directory where log files will be written.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> so that additional calls can be chained.</returns>
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string logsDirectory)
    {
        return builder.AddFile(options => options.Directory = logsDirectory);
    }

    /// <summary>Adds a file logger provider configured by the specified delegate.</summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the provider to.</param>
    /// <param name="configure">A delegate to configure the options of the provider.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> so that additional calls can be chained.</returns>
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        builder.AddFile();
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>Adds a file logger provider that writes to the specified directory and is configured by the specified delegate.</summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the provider to.</param>
    /// <param name="logsDirectory">The directory where log files will be written.</param>
    /// <param name="configure">A delegate to configure the options of the provider.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> so that additional calls can be chained.</returns>
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string logsDirectory, Action<FileLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddFile(options =>
        {
            options.Directory = logsDirectory;
            configure(options);
        });
    }
}
