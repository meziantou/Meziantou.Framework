namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class LiteralSegment : Segment
    {
        private readonly StringComparison _stringComparison;

        public LiteralSegment(string value, bool ignoreCase)
        {
            Value = value;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public string Value { get; private set; }

        public override bool IsMatch(ref PathReader pathReader)
        {
            if (pathReader.CurrentText.StartsWith(Value.AsSpan(), _stringComparison))
            {
                pathReader.ConsumeInSegment(Value.Length);
                return true;
            }

            return false;
        }

        public override string ToString()
        {
            return Value;
        }
    }
}
