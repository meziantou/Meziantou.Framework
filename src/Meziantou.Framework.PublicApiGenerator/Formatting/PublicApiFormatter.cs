namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Formats the symbols of a <see cref="PublicApiAssembly"/> as C# code.</summary>
public static class PublicApiFormatter
{
    private static readonly PublicApiFormattingOptions DefaultOptions = new();

    /// <summary>Formats a type or a member.</summary>
    /// <remarks>
    /// An accessor is formatted as its property, restricted to this accessor in the <see cref="PublicApiDeclarationStyle.Declaration"/> style, or as its event.
    /// </remarks>
    public static PublicApiFormattedDeclaration Format(PublicApiSymbol symbol, PublicApiFormattingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        options ??= DefaultOptions;

        var writer = new DeclarationWriter(options.NewLine);
        if (options.Style == PublicApiDeclarationStyle.Compilable)
        {
            new CSharpCompilableFormatter(options).WriteSymbol(writer, symbol);
        }
        else
        {
            new CSharpDeclarationFormatter(options).WriteSymbol(writer, symbol);
        }

        return writer.ToDeclaration();
    }

    /// <summary>Formats a type reference (e.g. <c>System.Collections.Generic.List&lt;string?&gt;</c>).</summary>
    public static PublicApiFormattedDeclaration Format(PublicApiTypeReference type, PublicApiFormattingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        options ??= DefaultOptions;

        var writer = new DeclarationWriter(options.NewLine);
        if (options.Style == PublicApiDeclarationStyle.Compilable)
        {
            CSharpCompilableFormatter.WriteTypeReference(writer, type, includeNullableAnnotations: true);
        }
        else
        {
            new CSharpDeclarationFormatter(options).WriteTypeReference(writer, type);
        }

        return writer.ToDeclaration();
    }

    /// <summary>Formats an attribute (e.g. <c>[System.Obsolete("Use another method")]</c>), whether or not the attribute filter selects it.</summary>
    public static PublicApiFormattedDeclaration Format(PublicApiAttribute attribute, PublicApiFormattingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        options ??= DefaultOptions;

        var writer = new DeclarationWriter(options.NewLine);
        new CSharpDeclarationFormatter(options).WriteAttribute(writer, attribute, target: null);
        return writer.ToDeclaration();
    }
}
