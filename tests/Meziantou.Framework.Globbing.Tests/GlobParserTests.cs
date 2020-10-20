using Meziantou.Framework.Globbing.Internals;
using Meziantou.Framework.Globbing.Internals.Segments;
using Xunit;

namespace Meziantou.Framework.Globbing.Tests
{
    public class GlobParserTests
    {
        private static Segment[] GetSegments(string pattern)
        {
            var glob = Glob.Parse(pattern, GlobOptions.None);
            return glob._segments;
        }

        private static Segment[] GetSubSegments(string pattern)
        {
            var glob = Glob.Parse(pattern, GlobOptions.None);
            Assert.Collection(glob._segments, item => Assert.IsType<RaggedSegment>(item));

            return ((RaggedSegment)glob._segments[0])._segments;
        }

        [Fact]
        public void OptimizeSegmentEndsWith()
        {
            var segments = GetSegments("*.txt");
            Assert.Collection(segments,
                item => Assert.IsType<EndsWithSegment>(item));
        }

        [Fact]
        public void OptimizeSegmentEndsWithWithPrefix()
        {
            var segments = GetSubSegments("p*.txt");
            Assert.Collection(segments,
                item => Assert.IsType<LiteralSegment>(item),
                item => Assert.IsType<EndsWithSegment>(item));
        }

        [Fact]
        public void OptimizeSegmentStartsWith()
        {
            var segments = GetSegments("file*");
            Assert.Collection(segments,
                item => Assert.IsType<StartsWithSegment>(item));
        }

        [Fact]
        public void OptimizeSegmentContains()
        {
            var segments = GetSegments("*file*");
            Assert.Collection(segments,
                item => Assert.IsType<ContainsSegment>(item));
        }

        [Fact]
        public void OptimizeSegmentConsecutiveStarts()
        {
            var segments = GetSegments("*file**");
            Assert.Collection(segments,
                item => Assert.IsType<ContainsSegment>(item));
        }

        [Fact]
        public void OptimizeSegmentStartsWithAndContains()
        {
            var segments = GetSubSegments("file*test*");
            Assert.Collection(segments,
                item => Assert.IsType<LiteralSegment>(item),
                item => Assert.IsType<ContainsSegment>(item));
        }

        [Fact]
        public void OptimizeSegmentLiteral()
        {
            var segments = GetSegments("test");
            Assert.Collection(segments,
                item => Assert.IsType<LiteralSegment>(item));
        }

        [Fact]
        public void OptimizeCombineTwoConsecutiveRecursiveMatchAll()
        {
            var segments = GetSegments("src/**/**/a/b");
            Assert.Collection(segments,
                item => Assert.IsType<LiteralSegment>(item),
                item => Assert.IsType<RecursiveMatchAllSegment>(item),
                item => Assert.IsType<LiteralSegment>(item),
                item => Assert.IsType<LiteralSegment>(item));
        }

        [Fact]
        public void OptimizeMatchLast()
        {
            var segments = GetSegments("a/**/b");
            Assert.Collection(segments,
                item => Assert.IsType<LiteralSegment>(item),
                item => Assert.IsType<LastSegment>(item));
        }

        [Fact]
        public void OptimizeMatchNonEmpty()
        {
            var segments = GetSegments("a/**/*");
            Assert.Collection(segments,
                item => Assert.IsType<LiteralSegment>(item),
                item => Assert.IsType<MatchNonEmptyTextSegment>(item));
        }

        [Fact]
        public void OptimizeStarConsumeUntil()
        {
            var segments = GetSubSegments("*[abc][b-c]");
            Assert.Collection(segments,
                item => Assert.IsType<ConsumeSegmentUntilSegment>(item),
                item => Assert.IsType<MatchAllSubSegment>(item),
                item => Assert.IsType<CharacterSetSegment>(item),
                item => Assert.IsType<CharacterRangeSegment>(item));
        }
    }
}
