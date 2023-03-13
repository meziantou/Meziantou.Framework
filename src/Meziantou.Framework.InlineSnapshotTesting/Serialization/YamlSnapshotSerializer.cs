using System.Globalization;
using System.Text;
using Meziantou.Framework.InlineSnapshotTesting.Utils;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

public sealed class YamlSnapshotSerializer : SnapshotSerializer
{
    private readonly ISerializer _serializer;

    internal static YamlSnapshotSerializer Instance { get; } = new();

    public YamlSnapshotSerializer()
    {
        _serializer = new SerializerBuilder()
            .WithIndentedSequences()
            .WithTypeConverter(new DateTimeOffsetConverter())
            .WithEventEmitter(nextEmitter => new MultilineScalarFlowStyleEmitter(nextEmitter))
            .Build();
    }

    public override string Serialize(object? value)
    {
        // YamlDotNet adds a new line at the end of the object
        var result = _serializer.Serialize(value).AsSpan();
        if (result.EndsWith(Environment.NewLine.AsSpan(), StringComparison.Ordinal))
            result = result[..^Environment.NewLine.Length];

        // YamlDotNet add trailing spaces
        var sb = new StringBuilder(result.Length);
        var first = true;
        foreach (var line in StringUtils.EnumerateLines(result))
        {
            if (!first)
                sb.AppendLine();

            sb.Append(line.TrimEnd(' '));
            first = false;
        }

        return sb.ToString();
    }

    private sealed class MultilineScalarFlowStyleEmitter : ChainedEventEmitter
    {
        public MultilineScalarFlowStyleEmitter(IEventEmitter nextEmitter)
            : base(nextEmitter)
        {
        }

        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (typeof(string).IsAssignableFrom(eventInfo.Source.Type))
            {
                var value = eventInfo.Source.Value as string;
                if (!string.IsNullOrEmpty(value))
                {
                    var isMultiLine = value.IndexOfAny(new char[] { '\r', '\n', '\x85', '\x2028', '\x2029' }) >= 0;
                    if (isMultiLine)
                    {
                        eventInfo = new ScalarEventInfo(eventInfo.Source)
                        {
                            Style = ScalarStyle.Literal,
                        };
                    }
                }
            }

            nextEmitter.Emit(eventInfo, emitter);
        }
    }

    private sealed class DateTimeOffsetConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(DateTimeOffset);

        public object ReadYaml(IParser parser, Type type) => throw new NotSupportedException();

        /// <summary>
        /// Writes the specified object's state to a YAML emitter.
        /// </summary>
        /// <param name="emitter"><see cref="IEmitter"/> instance.</param>
        /// <param name="value">Value to write.</param>
        /// <param name="type"><see cref="Type"/> to convert.</param>
        /// <remarks>On serializing, the first format in the list is used.</remarks>
        public void WriteYaml(IEmitter emitter, object? value, Type type)
        {
            var dt = (DateTimeOffset)value!;
            var formatted = dt.ToString("O", CultureInfo.InvariantCulture);
            emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, formatted, ScalarStyle.Any, isPlainImplicit: true, isQuotedImplicit: false));
        }
    }
}
