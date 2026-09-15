using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class YamlParserUtilities
{
    public static YamlFile? LoadYamlFile(ScanFileContext context)
    {
        try
        {
            using var textReader = new StreamReader(context.Content, leaveOpen: true);
            var text = textReader.ReadToEnd();
            var stream = YamlStream.Load(new StringReader(text));
            return new YamlFile(context, text, stream);
        }
        catch
        {
            return null;
        }
    }

    public static YamlElement? GetProperty(YamlElement? node, string propertyName, StringComparison stringComparison)
    {
        if (node is YamlMapping mapping)
        {
            foreach (var child in mapping)
            {
                if (child.Key is YamlValue scalar && string.Equals(scalar.Value, propertyName, stringComparison))
                    return child.Value;
            }
        }

        return null;
    }

    public static string? GetScalarValue(YamlElement? node)
    {
        if (node is YamlValue scalar)
            return scalar.Value;

        return null;
    }

    internal sealed class YamlFile
    {
        private readonly ScanFileContext _context;
        private readonly string _text;
        private readonly HashSet<int> _reportedScalars = [];

        public YamlFile(ScanFileContext context, string text, YamlStream stream)
        {
            _context = context;
            _text = text;
            Stream = stream;
        }

        public ScanFileContext Context => _context;

        public YamlStream Stream { get; }

        /// <summary>
        /// Aliases (<c>*name</c>) are expanded as copies of the anchored node that keep its source marks.
        /// Returns <see langword="false"/> when a scalar at the same source position was already reported, so the anchored node is reported once.
        /// </summary>
        public bool TryMarkAsReported(YamlElement? node)
        {
            if (node is not YamlValue scalar)
                return true;

            return _reportedScalars.Add(scalar.Scalar.Start.Index);
        }

        public void ReportDependencyWithSeparator(DependencyScanner scanner, YamlElement? node, DependencyType dependencyType, char versionSeparator)
        {
            var value = GetScalarValue(node);
            if (value is null || !TryMarkAsReported(node))
                return;

            var index = value.IndexOf(versionSeparator, StringComparison.Ordinal);
            if (index < 0)
            {
                _context.ReportDependency(scanner, name: value, version: null, dependencyType, nameLocation: GetLocation(node), versionLocation: null);
            }
            else
            {
                _context.ReportDependency(
                    scanner,
                    name: value[..index],
                    version: value[(index + 1)..],
                    dependencyType,
                    nameLocation: GetLocation(node, start: 0, length: index),
                    versionLocation: GetLocation(node, start: index + 1, length: value.Length - index - 1));
            }
        }

        public void ReportDockerImage(DependencyScanner scanner, YamlElement? node, int prefixLength = 0)
        {
            var value = GetScalarValue(node);
            if (value is null || value.Length <= prefixLength || !TryMarkAsReported(node))
                return;

            DockerImageReference.Report(scanner, _context, value[prefixLength..], (start, length) => GetLocation(node, prefixLength + start, length));
        }

        /// <summary>
        /// Gets the location of a range of a scalar value. The location is only updatable when the source text of the scalar is exactly its value,
        /// that is plain scalars and quoted scalars without escape sequences that fit on one line. Block scalars, escaped or multi-line scalars
        /// get a <see cref="NonUpdatableLocation"/>, as offsets in the value do not map to offsets in the file.
        /// </summary>
        public Location? GetLocation(YamlElement? node, int start = 0, int? length = null)
        {
            if (node is not YamlValue scalar)
                return null;

            var value = scalar.Value;
            length ??= Math.Max(0, value.Length - start);

            var scalarEvent = scalar.Scalar;
            var end = scalarEvent.End;
            var endOfValue = end.Index;
            switch (scalarEvent.Style)
            {
                case ScalarStyle.Plain:
                    break;

                case ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted:
                    var quote = scalarEvent.Style is ScalarStyle.SingleQuoted ? '\'' : '"';
                    endOfValue--;
                    if (endOfValue < 0 || endOfValue >= _text.Length || _text[endOfValue] != quote)
                        return new NonUpdatableLocation(_context);

                    break;

                default:
                    return new NonUpdatableLocation(_context);
            }

            // The start mark may include node properties (anchor, tag), so the value is located from the end mark
            var startOfValue = endOfValue - value.Length;
            if (startOfValue < scalarEvent.Start.Index || startOfValue < 0 || endOfValue > _text.Length || !_text.AsSpan(startOfValue, value.Length).SequenceEqual(value))
                return new NonUpdatableLocation(_context);

            // The column is computed from the end mark, which is only valid when the value is on the line of the end mark
            if (value.AsSpan().ContainsAny('\r', '\n'))
                return new NonUpdatableLocation(_context);

            var column = end.Column - (end.Index - startOfValue) + 1 + start;
            if (column < 1)
                return new NonUpdatableLocation(_context);

            return new TextLocation(_context.FileSystem, _context.FullPath, end.Line + 1, column, length.Value);
        }
    }
}
