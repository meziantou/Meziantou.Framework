namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class LastSegment : Segment
    {
        private readonly Segment _innerSegment;

        public LastSegment(Segment innerSegment)
        {
            _innerSegment = innerSegment;
        }

        public override bool IsMatch(ref PathReader pathReader)
        {
            pathReader.ConsumeToLastSegment();
            return _innerSegment.IsMatch(ref pathReader);
        }

        public override bool IsRecursiveMatchAll => true;
    }
}
