using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class ContainsSegment : Segment
    {
        private readonly string _value;
        private readonly StringComparison _stringComparison;

        public ContainsSegment(string value, bool ignoreCase)
        {
            _value = value;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public override bool IsMatch(ref PathReader pathReader)
        {
            var currentSegment = pathReader.CurrentSegment;
            if (currentSegment.Contains(_value.AsSpan(), _stringComparison))
            {
                pathReader.ConsumeInSegment(currentSegment.Length);
                return true;
            }

            return false;
        }

        public override string ToString()
        {
            return '*' + _value + '*';
        }
    }
}
