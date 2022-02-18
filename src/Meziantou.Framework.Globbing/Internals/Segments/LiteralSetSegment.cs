namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class LiteralSetSegment : Segment
    {
        private readonly StringComparison _comparison;

        public LiteralSetSegment(string[] values, bool ignoreCase)
        {
            Values = values;
            _comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public string[] Values { get; }

        public override bool IsMatch(ref PathReader pathReader)
        {
            foreach (var value in Values)
            {
                if (pathReader.CurrentText.StartsWith(value.AsSpan(), _comparison))
                {
                    pathReader.ConsumeInSegment(value.Length);
                    return true;
                }
            }

            return false;
        }

        public override string ToString()
        {
            using var sb = new ValueStringBuilder();
            sb.Append('{');

            var first = true;
            foreach (var value in Values)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                sb.Append(value);
                first = false;
            }

            sb.Append('}');
            return sb.ToString();
        }
    }
}
