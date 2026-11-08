namespace Meziantou.Framework.Barcodes.Internal.Gs1;

internal sealed record Gs1DictionaryComponent(char Type, int MinLength, int MaxLength, bool Optional, string[] Linters)
{
    public bool IsFixedLength => MinLength == MaxLength;

    public string CharacterSetLinter => Type switch
    {
        'N' => "csetnumeric",
        'Y' => "cset39",
        'Z' => "cset64",
        _ => "cset82",
    };

    public override string ToString() => (Optional ? "[" : "") + Type + (IsFixedLength ? "" : "..") + MaxLength.ToString(CultureInfo.InvariantCulture) + (Optional ? "]" : "");
}
