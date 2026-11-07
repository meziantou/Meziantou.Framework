using Meziantou.Framework.Json;

namespace Meziantou.Framework.ExternalJsonNavigatorTests;

public sealed class ExternalJsonNavigator : JsonPathNavigator<ExternalJsonNode>
{
    public int ElementAccessCount { get; private set; }

    public int ElementVisitCount { get; private set; }

    public int CopyElementsCount { get; private set; }

    public int CopiedElementCount { get; private set; }

    public override bool HasConstantTimeElementAccess => false;

    public override JsonPathNodeKind GetKind(ExternalJsonNode? value) => value?.Kind ?? JsonPathNodeKind.Null;

    public override bool TryGetPropertyValue(ExternalJsonNode? value, string name, out ExternalJsonNode? result)
    {
        if (value is not null && value.Properties.TryGetValue(name, out result))
        {
            return true;
        }

        result = null;
        return false;
    }

    public override IEnumerable<JsonPathProperty<ExternalJsonNode>> GetProperties(ExternalJsonNode? value)
    {
        if (value?.Kind is not JsonPathNodeKind.Object)
        {
            yield break;
        }

        foreach (var (name, propertyValue) in value.Properties)
        {
            yield return new JsonPathProperty<ExternalJsonNode>(name, propertyValue);
        }
    }

    public override int GetArrayLength(ExternalJsonNode? value) => value?.Kind is JsonPathNodeKind.Array ? value.Items.Count : 0;

    public override bool TryGetElement(ExternalJsonNode? value, int index, out ExternalJsonNode? result)
    {
        ElementAccessCount++;
        if (value?.Kind is JsonPathNodeKind.Array && index >= 0 && index < value.Items.Count)
        {
            var item = value.Items.First;
            for (var i = 0; i < index; i++)
            {
                item = item!.Next;
                ElementVisitCount++;
            }

            ElementVisitCount++;
            result = item!.Value;
            return true;
        }

        result = null;
        return false;
    }

    public override void CopyElements(ExternalJsonNode? value, ExternalJsonNode?[] destination)
    {
        CopyElementsCount++;
        if (value?.Kind is not JsonPathNodeKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var item in value.Items)
        {
            destination[index] = item;
            index++;
            CopiedElementCount++;
        }
    }

    public override bool TryGetString(ExternalJsonNode? value, out string? result)
    {
        result = value?.Kind is JsonPathNodeKind.String ? value.StringValue : null;
        return result is not null;
    }

    public override bool TryGetNumber(ExternalJsonNode? value, out double result)
    {
        if (value?.Kind is JsonPathNodeKind.Number)
        {
            result = value.NumberValue;
            return true;
        }

        result = 0;
        return false;
    }

    public override bool TryGetBoolean(ExternalJsonNode? value, out bool result)
    {
        if (value?.Kind is JsonPathNodeKind.Boolean)
        {
            result = value.BooleanValue;
            return true;
        }

        result = false;
        return false;
    }

    public void ResetCounters()
    {
        ElementAccessCount = 0;
        ElementVisitCount = 0;
        CopyElementsCount = 0;
        CopiedElementCount = 0;
    }
}
