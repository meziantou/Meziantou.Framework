namespace Meziantou.Framework.Pdf;

public sealed partial class PdfInteractiveForm
{
    /// <summary>
    /// Exports the values of the terminal fields, which <see cref="PdfFormData.ToFdf"/> and
    /// <see cref="PdfFormData.ToXfdf"/> write. The push buttons and the signature fields have no value to export.
    /// </summary>
    /// <returns>The values, in the order of the fields.</returns>
    public PdfFormData ExportData()
    {
        var data = new PdfFormData();
        foreach (var field in GetFields())
        {
            switch (field)
            {
                case PdfPushButtonField or PdfSignatureField:
                    continue;
                case PdfChoiceField choice:
                    data.Fields.Add(new PdfFormDataField(field.FullName, choice.SelectedValues));
                    break;
                case PdfCheckBoxField or PdfRadioButtonField:
                    data.Fields.Add(new PdfFormDataField(field.FullName, field.Value ?? "Off") { IsName = true });
                    break;
                default:
                    data.Fields.Add(new PdfFormDataField(field.FullName, field.Value));
                    break;
            }
        }

        return data;
    }

    /// <summary>
    /// Sets the values of the fields from form data, read from FDF (<see cref="PdfFormData.ParseFdf"/>) or XFDF
    /// (<see cref="PdfFormData.ParseXfdf(string)"/>): each value is set as <see cref="PdfFormField.Value"/> and
    /// <see cref="PdfChoiceField.SetSelectedValues"/> set it, so the appearances of the widgets are written again.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The fields updated, the names the form does not have, and the values rejected.</returns>
    /// <remarks>A name given several times sets the field several times: the last value is kept.</remarks>
    public PdfFormImportResult ImportData(PdfFormData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var updated = new List<string>();
        var unknown = new List<string>();
        var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in data.Fields)
        {
            var field = FindField(item.Name);
            if (field is null)
            {
                unknown.Add(item.Name);
                continue;
            }

            if (!field.IsTerminal)
            {
                rejected[item.Name] = "The field is not a terminal field: its value is the value of its children";
                continue;
            }

            try
            {
                switch (field)
                {
                    case PdfPushButtonField or PdfSignatureField:
                        rejected[item.Name] = "The field has no value";
                        continue;
                    case PdfChoiceField choice:
                        choice.SetSelectedValues(item.Values);
                        break;
                    case PdfCheckBoxField or PdfRadioButtonField:
                        field.Value = item.Value is "Off" ? null : item.Value;
                        break;
                    default:
                        field.Value = item.Value;
                        break;
                }

                rejected.Remove(item.Name);
                updated.Add(item.Name);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or PdfException)
            {
                rejected[item.Name] = ex.Message;
            }
        }

        return new PdfFormImportResult(updated, unknown, rejected);
    }
}
