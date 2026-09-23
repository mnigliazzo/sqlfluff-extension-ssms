using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class NoqaCommentEditorTests
    {
        [Fact]
        public void AddNoqa_AppendsCommentToPlainLine()
        {
            Assert.Equal(
                "SELECT * FROM foo  -- noqa: AL01",
                NoqaCommentEditor.AddNoqa("SELECT * FROM foo", "AL01"));
        }

        [Fact]
        public void AddNoqa_TrimsTrailingWhitespaceBeforeAppending()
        {
            Assert.Equal(
                "SELECT * FROM foo  -- noqa: AL01",
                NoqaCommentEditor.AddNoqa("SELECT * FROM foo   ", "AL01"));
        }

        [Fact]
        public void AddNoqa_OnEmptyLineHasNoLeadingSeparator()
        {
            Assert.Equal("-- noqa: AL01", NoqaCommentEditor.AddNoqa("", "AL01"));
        }

        [Fact]
        public void AddNoqa_AddsRuleToExistingNoqaList()
        {
            Assert.Equal(
                "SELECT * FROM foo -- noqa: AL01,LT01",
                NoqaCommentEditor.AddNoqa("SELECT * FROM foo -- noqa: AL01", "LT01"));
        }

        [Fact]
        public void AddNoqa_IsNoOpWhenRuleAlreadySuppressed()
        {
            string line = "SELECT * FROM foo -- noqa: AL01,LT01";
            Assert.Equal(line, NoqaCommentEditor.AddNoqa(line, "AL01"));
        }

        [Fact]
        public void AddNoqa_IsCaseInsensitiveWhenCheckingForDuplicates()
        {
            string line = "SELECT * FROM foo -- noqa: al01";
            Assert.Equal(line, NoqaCommentEditor.AddNoqa(line, "AL01"));
        }

        [Fact]
        public void AddNoqa_IsNoOpWhenLineAlreadyHasBareNoqa()
        {
            string line = "SELECT * FROM foo -- noqa";
            Assert.Equal(line, NoqaCommentEditor.AddNoqa(line, "AL01"));
        }

        [Fact]
        public void AddNoqa_AppendsAfterUnrelatedTrailingComment()
        {
            Assert.Equal(
                "SELECT * FROM foo -- some other comment  -- noqa: AL01",
                NoqaCommentEditor.AddNoqa("SELECT * FROM foo -- some other comment", "AL01"));
        }

        [Fact]
        public void AddNoqa_ReturnsUnchangedWhenRuleCodeIsEmpty()
        {
            string line = "SELECT * FROM foo";
            Assert.Equal(line, NoqaCommentEditor.AddNoqa(line, ""));
            Assert.Equal(line, NoqaCommentEditor.AddNoqa(line, null));
        }
    }
}
