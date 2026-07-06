namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Base type for source-generated YAML serializer contexts.</summary>
public abstract partial class YamlSerializerContext : IYamlTypeInfoResolver
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlSerializerContext"/> class.
    /// </summary>
    protected YamlSerializerContext()
        : this(new YamlSerializerOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlSerializerContext"/> class.
    /// </summary>
    /// <param name="options">The options used by this context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> specifies a <see cref="YamlSerializerOptions.TypeInfoResolver"/> that is not this context.
    /// </exception>
    protected YamlSerializerContext(YamlSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.TypeInfoResolver is null)
        {
            Options = options with { TypeInfoResolver = this };
            return;
        }

        if (!ReferenceEquals(options.TypeInfoResolver, this))
        {
            throw new ArgumentException(
                $"The provided {nameof(YamlSerializerOptions)} instance is associated with a different {nameof(YamlSerializerOptions.TypeInfoResolver)}. " +
                $"A {nameof(YamlSerializerContext)} must use an options instance whose {nameof(YamlSerializerOptions.TypeInfoResolver)} is the context itself.",
                nameof(options));
        }

        Options = options;
    }

    /// <summary>Gets the options instance associated with this context.</summary>
    /// <remarks>This is the options instance used by generated metadata properties on the context.</remarks>
    public YamlSerializerOptions Options { get; }

    /// <summary>
    /// Creates a new <see cref="YamlSerializerOptions"/> based on this context's options
    /// with the specified overrides applied, while preserving the <see cref="YamlSerializerOptions.TypeInfoResolver"/>.
    /// </summary>
    /// <param name="configure">A function that applies overrides to a copy of this context's options using the <c>with</c> expression.</param>
    /// <returns>A new options instance with the configured overrides and this context as the resolver.</returns>
    /// <remarks>
    /// <para>
    /// This is useful when you need per-call option variations (such as a different
    /// <see cref="YamlSerializerOptions.SourceName"/> for each file) while reusing the
    /// same source-generated context.
    /// </para>
    /// <para>
    /// The returned options always has <see cref="YamlSerializerOptions.TypeInfoResolver"/> set to this context.
    /// Any <see cref="YamlSerializerOptions.TypeInfoResolver"/> value set by <paramref name="configure"/> is overwritten.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = context.CreateOptions(o =&gt; o with { SourceName = "config.yaml" });
    /// var result = YamlSerializer.Deserialize&lt;Config&gt;(yaml, options);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public YamlSerializerOptions CreateOptions(Func<YamlSerializerOptions, YamlSerializerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var modified = configure(Options);
        if (ReferenceEquals(modified.TypeInfoResolver, this))
        {
            return modified;
        }

        return modified with { TypeInfoResolver = this };
    }

    /// <inheritdoc />
    public abstract YamlTypeInfo? GetTypeInfo(Type type, YamlSerializerOptions options);
}
