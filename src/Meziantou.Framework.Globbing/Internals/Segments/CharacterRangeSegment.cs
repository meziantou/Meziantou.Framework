namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class CharacterRangeSegment : Segment
    {
        public CharacterRangeSegment(CharacterRange range)
        {
            Range = range;
        }

        internal CharacterRange Range { get; }

        internal static bool IsAsciiUpper(int c)
        {
            return c >= 'A' && c <= 'Z';
        }

        public override bool IsMatch(ref PathReader pathReader)
        {
            var result = Range.IsInRange(pathReader.CurrentText[0]);
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
            sb.Append(Range.Min);
            sb.Append('-');
            sb.Append(Range.Max);
            sb.Append(']');
            return sb.ToString();
        }
    }
}
