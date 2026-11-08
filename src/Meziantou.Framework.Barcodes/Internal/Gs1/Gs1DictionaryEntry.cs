namespace Meziantou.Framework.Barcodes.Internal.Gs1;

// Requirements: each item must be satisfied; an item is satisfied when one of its alternatives is, and an alternative is
// satisfied when every pattern it lists matches an element (req=01+21,02 requires (01) with (21), or (02)).
internal sealed record Gs1DictionaryEntry(string ApplicationIdentifier, string Title, bool PredefinedLength, Gs1DictionaryComponent[] Components, string[][][] Requirements, string[] Exclusions)
{
    public string Format => string.Join(' ', Components.Select(component => component.ToString()));
}
