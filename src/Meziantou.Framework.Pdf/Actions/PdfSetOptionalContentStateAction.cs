using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that turns optional content groups (layers) on or off (<c>SetOCGState</c>, ISO 32000-2, 12.6.4.13).</summary>
public sealed class PdfSetOptionalContentStateAction : PdfAction
{
    internal PdfSetOptionalContentStateAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the changes (<c>/State</c>), in order: each group with the state it is set to.</summary>
    public IReadOnlyList<(PdfOptionalContentStateChange State, PdfOptionalContentGroup Group)> Changes
    {
        get
        {
            var result = new List<(PdfOptionalContentStateChange, PdfOptionalContentGroup)>();
            if (Dictionary.Get<PdfArray>(PdfName.Create("State")) is not { } state || Document is null)
                return result;

            PdfOptionalContentStateChange? current = null;
            for (var i = 0; i < state.Count && result.Count < MaxActions * 16; i++)
            {
                switch (state[i])
                {
                    case PdfName name:
                        current = name.Value switch
                        {
                            "ON" => PdfOptionalContentStateChange.On,
                            "OFF" => PdfOptionalContentStateChange.Off,
                            "Toggle" => PdfOptionalContentStateChange.Toggle,
                            _ => null,
                        };
                        break;
                    case PdfReference reference when current is { } change && reference.Resolve() is PdfDictionary group:
                        result.Add((change, new PdfOptionalContentGroup(Document, group, reference)));
                        break;
                }
            }

            return result;
        }
    }

    /// <summary>Gets a value indicating whether the radio-button groups of the configuration are kept (<c>/PreserveRB</c>, the default): turning a group on turns the others of its radio-button groups off.</summary>
    public bool PreserveRadioButtons => Dictionary.GetBoolean(PdfName.Create("PreserveRB"), defaultValue: true);
}
