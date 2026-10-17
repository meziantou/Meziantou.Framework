using System.Numerics;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The operations of IndexedDB on the .NET values, used without script engine: the keys are the numbers, the strings, the
/// dates (<see cref="DateTime"/>, <see cref="DateTimeOffset"/>), the byte arrays and the lists of keys; the values are the
/// primitive values and the serializable platform objects (e.g. a <see cref="Blob"/>). The keys cannot be injected.
/// </summary>
internal sealed class DotNetIndexedDBValueOperations : IIndexedDBValueOperations
{
    public static DotNetIndexedDBValueOperations Instance { get; } = new();

    public IdbKey? ConvertToKey(object? value, bool multiEntry = false)
    {
        if (multiEntry && value is IEnumerable<object?> items)
        {
            var keys = new List<IdbKey>();
            foreach (var item in items)
            {
                if (ConvertToKey(item, multiEntry: false) is { } key && !keys.Contains(key))
                {
                    keys.Add(key);
                }
            }

            return IdbKey.FromArray([.. keys]);
        }

        switch (value)
        {
            case string text:
                return IdbKey.FromString(text);
            case byte[] bytes:
                return IdbKey.FromBinary([.. bytes]);
            case DateTime dateTime:
                return IdbKey.FromDate(new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeMilliseconds());
            case DateTimeOffset dateTimeOffset:
                return IdbKey.FromDate(dateTimeOffset.ToUnixTimeMilliseconds());
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
            {
                var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                return double.IsNaN(number) ? null : IdbKey.FromNumber(number);
            }

            case IEnumerable<object?> list:
            {
                var keys = new List<IdbKey>();
                foreach (var item in list)
                {
                    if (ConvertToKey(item) is not { } key)
                        return null;

                    keys.Add(key);
                }

                return IdbKey.FromArray([.. keys]);
            }

            default:
                return null;
        }
    }

    public object? ConvertToValue(IdbKey key) => key.Type switch
    {
        IdbKeyType.Number => key.Number,
        IdbKeyType.String => key.String,
        IdbKeyType.Date => DateTimeOffset.FromUnixTimeMilliseconds((long)key.Number).UtcDateTime,
        IdbKeyType.Binary => key.Binary!.ToArray(),
        _ => key.Items!.Select(ConvertToValue).ToArray(),
    };

    public bool TryEvaluateKeyPath(object? value, string keyPath, out object? result)
    {
        result = value;
        if (keyPath.Length == 0)
            return true;

        foreach (var identifier in keyPath.Split('.'))
        {
            switch (result, identifier)
            {
                case (string text, "length"):
                    result = (double)text.Length;
                    break;
                case (Blob blob, "size"):
                    result = (double)blob.Size;
                    break;
                case (Blob blob, "type"):
                    result = blob.Type;
                    break;
                case (Dom.FileApi.File file, "name"):
                    result = file.Name;
                    break;
                case (Dom.FileApi.File file, "lastModified"):
                    result = (double)file.LastModified;
                    break;
                default:
                    result = null;
                    return false;
            }
        }

        return true;
    }

    public bool CanInjectKey(object? value, string keyPath) => false;

    public void InjectKey(object? value, IdbKey key, string keyPath) => throw new NotSupportedException("A key cannot be injected into a .NET value");

    public AgentSerializedValue SerializeForStorage(object? value) => new(value switch
    {
        null => UndefinedValue.Instance,
        NullValue => null,
        string or bool or double or BigInteger => value,
        byte or sbyte or short or ushort or int or uint or long or ulong or float or decimal => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
        IStructuredSerializable serializable => new ExportedPlatformObject(serializable.CloneForStructuredSerialization()),
        _ => throw new DomException($"An object of type '{value.GetType().Name}' could not be cloned", "DataCloneError"),
    });

    public bool IsNullOrUndefined(object? value) => value is null or NullValue;

    public bool IsPotentiallyValidKey(object? value) => value is string or byte[] or DateTime or DateTimeOffset or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or IEnumerable<object?>;

    public object CreateArray(IReadOnlyList<object?> items) => items.ToArray();

    public IdbGetAllOptions ConvertGetAllOptions(object? value) => value as IdbGetAllOptions ?? new IdbGetAllOptions();

    public object? Deserialize(AgentSerializedValue serialized) => serialized.Root switch
    {
        UndefinedValue => null,
        null => NullValue.Instance,
        ExportedPlatformObject { Value: IStructuredSerializable serializable } => serializable.CloneForStructuredSerialization(),
        var root => root,
    };
}
