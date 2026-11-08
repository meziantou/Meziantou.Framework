namespace Meziantou.Framework.Barcodes.Internal.Gs1;

// Validates element strings against the GS1 Barcode Syntax Dictionary, as the reference GS1 Barcode Syntax Engine does
internal static class Gs1DictionaryValidator
{
    public readonly record struct ValueError(Gs1LintError Error, int Position, int Length, string Message);

    public static void Validate(string applicationIdentifier, string value)
    {
        var entry = Gs1SyntaxDictionary.Find(applicationIdentifier)
            ?? throw new ArgumentException($"The application identifier ({applicationIdentifier}) is not in the GS1 dictionary of application identifiers.", nameof(applicationIdentifier));

        var error = ValidateValue(entry, value);
        if (error is { } e)
        {
            var title = entry.Title.Length > 0 ? " " + entry.Title : "";
            throw new ArgumentException($"The data of the application identifier ({applicationIdentifier}){title} does not match the format {entry.Format} at position {e.Position.ToString(CultureInfo.InvariantCulture)}: {e.Message}", nameof(value));
        }
    }

    // Each component consumes a prefix of the data: its fixed length, or up to its maximum length for the last
    // component. The optional components are ignored once the data is exhausted.
    public static ValueError? ValidateValue(Gs1DictionaryEntry entry, string value)
    {
        var position = 0;
        foreach (var component in entry.Components)
        {
            var remaining = value.Length - position;
            if (remaining == 0 && component.Optional)
                break;

            if (remaining < component.MinLength)
                return new ValueError(Gs1LintError.None, position, remaining, $"The data is too short, the component {component} needs {component.MinLength.ToString(CultureInfo.InvariantCulture)} characters.");

            var length = Math.Min(remaining, component.MaxLength);
            var data = value.AsSpan(position, length);
            var result = Gs1Linters.Lint(component.CharacterSetLinter, data);
            if (result.IsValid)
            {
                foreach (var linter in component.Linters)
                {
                    result = Gs1Linters.Lint(linter, data);
                    if (!result.IsValid)
                        break;
                }
            }

            if (!result.IsValid)
                return new ValueError(result.Error, position + result.Position, result.Length, Gs1Linters.GetMessage(result.Error));

            position += length;
        }

        if (position < value.Length)
            return new ValueError(Gs1LintError.None, position, value.Length - position, $"The data is too long, it has at most {position.ToString(CultureInfo.InvariantCulture)} characters.");

        return null;
    }

    public static void ValidateAssociations(ReadOnlySpan<Gs1Element> elements, string paramName)
    {
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i] ?? throw new ArgumentException("The elements cannot contain null.", paramName);
            for (var j = 0; j < i; j++)
            {
                var other = elements[j];
                if (other.ApplicationIdentifier == element.ApplicationIdentifier && other.Value != element.Value)
                    throw new ArgumentException($"The application identifier ({element.ApplicationIdentifier}) appears twice with different data.", paramName);
            }
        }

        foreach (var element in elements)
        {
            var entry = Gs1SyntaxDictionary.Find(element.ApplicationIdentifier);
            if (entry is null)
                continue;

            // An application identifier is not exclusive of itself, even when it matches a pattern
            foreach (var pattern in entry.Exclusions)
            {
                foreach (var other in elements)
                {
                    if (other.ApplicationIdentifier != element.ApplicationIdentifier && Gs1SyntaxDictionary.MatchesPattern(other.ApplicationIdentifier, pattern))
                        throw new ArgumentException($"The application identifiers ({element.ApplicationIdentifier}) and ({other.ApplicationIdentifier}) cannot be used together.", paramName);
                }
            }

            foreach (var requirement in entry.Requirements)
            {
                if (!IsSatisfied(requirement, elements))
                {
                    var alternatives = string.Join(", or ", requirement.Select(alternative => string.Join(" with ", alternative.Select(pattern => "(" + pattern + ")"))));
                    throw new ArgumentException($"The application identifier ({element.ApplicationIdentifier}) requires {alternatives}.", paramName);
                }
            }
        }
    }

    private static bool IsSatisfied(string[][] requirement, ReadOnlySpan<Gs1Element> elements)
    {
        foreach (var alternative in requirement)
        {
            var satisfied = true;
            foreach (var pattern in alternative)
            {
                if (!Contains(elements, pattern))
                {
                    satisfied = false;
                    break;
                }
            }

            if (satisfied)
                return true;
        }

        return false;

        static bool Contains(ReadOnlySpan<Gs1Element> elements, string pattern)
        {
            foreach (var element in elements)
            {
                if (Gs1SyntaxDictionary.MatchesPattern(element.ApplicationIdentifier, pattern))
                    return true;
            }

            return false;
        }
    }
}
