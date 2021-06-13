using FluentAssertions;
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
            glob._segments.Should().AllBeOfType<RaggedSegment>();

            return ((RaggedSegment)glob._segments[0])._segments;
        }

        [Fact]
        public void OptimizeSegmentEndsWith()
        {
            var segments = GetSegments("*.txt");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<EndsWithSegment>());
        }

        [Fact]
        public void OptimizeSegmentEndsWithWithPrefix()
        {
            var segments = GetSubSegments("p*.txt");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<LiteralSegment>(),
                item => item.Should().BeOfType<EndsWithSegment>());
        }

        [Fact]
        public void OptimizeSegmentStartsWith()
        {
            var segments = GetSegments("file*");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<StartsWithSegment>());
        }

        [Fact]
        public void OptimizeSegmentContains()
        {
            var segments = GetSegments("*file*");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<ContainsSegment>());
        }

        [Fact]
        public void OptimizeSegmentConsecutiveStarts()
        {
            var segments = GetSegments("*file**");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<ContainsSegment>());
        }

        [Fact]
        public void OptimizeSegmentStartsWithAndContains()
        {
            var segments = GetSubSegments("file*test*");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<LiteralSegment>(),
                item => item.Should().BeOfType<ContainsSegment>());
        }

        [Fact]
        public void OptimizeSegmentLiteral()
        {
            var segments = GetSegments("test");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<LiteralSegment>());
        }

        [Fact]
        public void OptimizeCombineTwoConsecutiveRecursiveMatchAll()
        {
            var segments = GetSegments("src/**/**/a/b");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<LiteralSegment>(),
                item => item.Should().BeOfType<RecursiveMatchAllSegment>(),
                item => item.Should().BeOfType<LiteralSegment>(),
                item => item.Should().BeOfType<LiteralSegment>());
        }

        [Fact]
        public void OptimizeMatchLast()
        {
            var segments = GetSegments("a/**/b");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<LiteralSegment>(),
                item => item.Should().BeOfType<LastSegment>());
        }

        [Fact]
        public void OptimizeMatchNonEmpty()
        {
            var segments = GetSegments("a/**/*");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<LiteralSegment>(),
                item => item.Should().BeOfType<MatchNonEmptyTextSegment>());
        }

        [Fact]
        public void OptimizeStarConsumeUntil()
        {
            var segments = GetSubSegments("*[abc][b-c]");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<ConsumeSegmentUntilSegment>(),
                item => item.Should().BeOfType<MatchAllSubSegment>(),
                item => item.Should().BeOfType<CharacterSetSegment>(),
                item => item.Should().BeOfType<CharacterRangeSegment>());
        }

        [Fact]
        public void OptimizeSingleCharSet()
        {
            var segments = GetSubSegments("*[a-b]def[a][b][c]abc[a][a-b]");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<ConsumeSegmentUntilSegment>(),
                item => item.Should().BeOfType<MatchAllSubSegment>(),
                item => item.Should().BeOfType<CharacterRangeSegment>(),
                item => item.Should().BeOfType<LiteralSegment>(),
                item => item.Should().BeOfType<CharacterRangeSegment>());
        }

        [Fact]
        public void OptimizeSingleCharSet2()
        {
            var segments = GetSubSegments("*[!a]def[a][b][c]abc[a][a-b]");
            segments.Should().SatisfyRespectively(
                item => item.Should().BeOfType<MatchAllSubSegment>(),
                item => item.Should().BeOfType<CharacterSetInverseSegment>(),
                item => item.Should().BeOfType<LiteralSegment>(),
                item => item.Should().BeOfType<CharacterRangeSegment>());
        }
    }
}
