using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The changes <see cref="PdfOptimizer"/> plans for a document. Inspect the <see cref="Actions"/>, disable the ones to
/// leave out (<see cref="PdfOptimizationAction.IsEnabled"/>), and apply the plan with
/// <see cref="PdfOptimizer.Apply(PdfOptimizationPlan)"/>.
/// </summary>
public sealed class PdfOptimizationPlan
{
    internal PdfOptimizationPlan(PdfDocument document, PdfOptimizationOptions options, IReadOnlyList<PdfOptimizationAction> actions, bool hasSignatures)
    {
        Document = document;
        Options = options;
        Actions = actions;
        HasSignatures = hasSignatures;
    }

    /// <summary>Gets the document the plan was created for.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the options the plan was created with.</summary>
    public PdfOptimizationOptions Options { get; }

    /// <summary>Gets the changes, in the order they are made.</summary>
    public IReadOnlyList<PdfOptimizationAction> Actions { get; }

    /// <summary>Gets a value indicating whether the document has signed signature fields.</summary>
    public bool HasSignatures { get; }

    /// <summary>Gets a value indicating whether applying the plan invalidates signatures: the document is signed, and an action is enabled.</summary>
    public bool InvalidatesSignatures => HasSignatures && Actions.Any(action => action.IsEnabled);

    /// <summary>Writes the plan as JSON: <c>hasSignatures</c>, <c>invalidatesSignatures</c> and the <c>actions</c>, with their kind, description, page index, object number, resource name and state.</summary>
    /// <param name="indented"><see langword="true"/> to indent the JSON.</param>
    /// <returns>The JSON text.</returns>
    public string ToJson(bool indented = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("hasSignatures", HasSignatures);
            writer.WriteBoolean("invalidatesSignatures", InvalidatesSignatures);
            writer.WriteStartArray("actions");
            foreach (var action in Actions)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", action.Kind.ToString());
                writer.WriteString("description", action.Description);
                if (action.PageIndex is { } pageIndex)
                {
                    writer.WriteNumber("pageIndex", pageIndex);
                }

                if (action.ObjectNumber is { } objectNumber)
                {
                    writer.WriteNumber("objectNumber", objectNumber);
                }

                if (action.ResourceName is { } resourceName)
                {
                    writer.WriteString("resourceName", resourceName);
                }

                writer.WriteBoolean("enabled", action.IsEnabled);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }
}
