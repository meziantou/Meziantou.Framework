namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class CharacterRangeSegment : Segment
    {
        private readonly CharacterRange _range;

        public CharacterRangeSegment(CharacterRange range)
        {
            _range = range;
        }

        internal static bool IsAsciiUpper(int c)
        {
            return c >= 'A' && c <= 'Z';
        }

        public override bool IsMatch(ref PathReader pathReader)
        {
            var result = _range.IsInRange(pathReader.CurrentText[0]);
            if (result)
            {
                pathReader.ConsumeInSegment(1);
            }

            return result;
        }

        public override string ToString()
        {
            using var sb = new ValueStringBuilder();
            sb.Append('[');
            sb.Append(_range.Min);
            sb.Append('-');
            sb.Append(_range.Max);
            sb.Append(']');
            return sb.ToString();
        }
    }
}
