using Meziantou.Framework.Barcodes.Internal.Linear;

namespace Meziantou.Framework.Barcodes.Internal;

internal static class Gs1ElementStrings
{
    /// <summary>
    /// Concatenates the element strings, with <paramref name="separator"/> after each element whose application identifier
    /// does not have a predefined length, except the last one: the separator stands for a FNC1.
    /// </summary>
    public static string Concatenate(ReadOnlySpan<Gs1Element> elements, char separator, string paramName)
    {
        var data = new StringBuilder();
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            if (element is null)
                throw new ArgumentException("The elements cannot contain null.", paramName);

            data.Append(element.ApplicationIdentifier).Append(element.Value);
            if (i < elements.Length - 1 && Gs1128Encoder.GetPredefinedValueLength(element.ApplicationIdentifier) < 0)
            {
                data.Append(separator);
            }
        }

        return data.ToString();
    }
}
