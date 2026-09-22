using System.Linq;
using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class LineDiffTests
    {
        [Fact]
        public void ComputeHunks_ReturnsNoHunks_WhenLinesAreIdentical()
        {
            string[] original = { "SELECT 1", "FROM t" };

            var hunks = LineDiff.ComputeHunks(original, original);

            Assert.Empty(hunks);
        }

        [Fact]
        public void ApplySelectedHunks_KeepsOnlyTheAcceptedHunk_DiscardsOthers()
        {
            // Two independent single-line changes far apart in the file.
            string[] original = { "select a", "b", "c", "d", "select b" };
            string[] rewritten = { "SELECT a", "b", "c", "d", "SELECT b" };

            var hunks = LineDiff.ComputeHunks(original, rewritten);
            Assert.Equal(2, hunks.Count);

            // Accept only the hunk touching original line 1 (1-based) — the first "select".
            var merged = LineDiff.ApplySelectedHunks(original, hunks, h => LineDiff.HunkTouchesLine(h, 1));

            Assert.Equal(new[] { "SELECT a", "b", "c", "d", "select b" }, merged);
        }

        [Fact]
        public void ApplySelectedHunks_CanAcceptTheOtherHunkInstead()
        {
            string[] original = { "select a", "b", "c", "d", "select b" };
            string[] rewritten = { "SELECT a", "b", "c", "d", "SELECT b" };

            var hunks = LineDiff.ComputeHunks(original, rewritten);

            var merged = LineDiff.ApplySelectedHunks(original, hunks, h => LineDiff.HunkTouchesLine(h, 5));

            Assert.Equal(new[] { "select a", "b", "c", "d", "SELECT b" }, merged);
        }

        [Fact]
        public void ApplySelectedHunks_AcceptingNoHunks_ReturnsOriginalUnchanged()
        {
            string[] original = { "select a", "b", "select b" };
            string[] rewritten = { "SELECT a", "b", "SELECT b" };

            var hunks = LineDiff.ComputeHunks(original, rewritten);
            var merged = LineDiff.ApplySelectedHunks(original, hunks, _ => false);

            Assert.Equal(original, merged);
        }

        [Fact]
        public void ApplySelectedHunks_AcceptingAllHunks_ReturnsFullRewrite()
        {
            string[] original = { "select a", "b", "select b" };
            string[] rewritten = { "SELECT a", "b", "SELECT b" };

            var hunks = LineDiff.ComputeHunks(original, rewritten);
            var merged = LineDiff.ApplySelectedHunks(original, hunks, _ => true);

            Assert.Equal(rewritten, merged);
        }

        [Fact]
        public void ComputeHunks_HandlesPureInsertion()
        {
            string[] original = { "a", "c" };
            string[] rewritten = { "a", "b", "c" };

            var hunks = LineDiff.ComputeHunks(original, rewritten);

            var hunk = Assert.Single(hunks);
            Assert.Equal(1, hunk.OriginalStart);
            Assert.Equal(1, hunk.OriginalEnd); // empty original range: pure insertion
            Assert.Equal(new[] { "b" }, hunk.NewLines);
        }

        [Fact]
        public void HunkTouchesLine_AnchorsPureInsertionToTheFollowingOriginalLine()
        {
            string[] original = { "a", "c" };
            string[] rewritten = { "a", "b", "c" };
            LineHunk hunk = LineDiff.ComputeHunks(original, rewritten).Single();

            // The insertion sits before original line 2 ("c"), so it's anchored there.
            Assert.True(LineDiff.HunkTouchesLine(hunk, 2));
            Assert.False(LineDiff.HunkTouchesLine(hunk, 1));
        }

        [Fact]
        public void ComputeHunks_HandlesPureDeletionAtEndOfFile()
        {
            string[] original = { "a", "b", "c" };
            string[] rewritten = { "a" };

            var hunks = LineDiff.ComputeHunks(original, rewritten);

            var hunk = Assert.Single(hunks);
            Assert.Equal(1, hunk.OriginalStart);
            Assert.Equal(3, hunk.OriginalEnd);
            Assert.Empty(hunk.NewLines);
        }

        [Fact]
        public void ApplySelectedHunks_WithNoHunks_ReturnsOriginal()
        {
            string[] original = { "a", "b" };

            var merged = LineDiff.ApplySelectedHunks(original, LineDiff.ComputeHunks(original, original), _ => true);

            Assert.Equal(original, merged);
        }
    }
}
