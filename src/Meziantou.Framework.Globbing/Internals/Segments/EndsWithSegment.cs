using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class EndsWithSegment : Segment
    {
        private readonly StringComparison _stringComparison;

        public EndsWithSegment(string value, bool ignoreCase)
        {
            Value = value;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public string Value { get; }

        public override bool IsMatch(ref PathReader pathReader)
        {
            var currentSegment = pathReader.CurrentSegment;
            if (currentSegment.EndsWith(Value.AsSpan(), _stringComparison))
            {
                pathReader.ConsumeInSegment(currentSegment.Length);
                return true;
            }

            return false;
        }

        public override string ToString()
        {
            return '*' + Value;
        }
    }
}
