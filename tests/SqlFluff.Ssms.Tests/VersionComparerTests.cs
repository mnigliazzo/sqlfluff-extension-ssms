using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class VersionComparerTests
    {
        [Theory]
        [InlineData("1.5.0", "1.6.0", true)]
        [InlineData("1.5.0", "1.5.0", false)]
        [InlineData("1.6.0", "1.5.0", false)]
        [InlineData("1.5.0", "1.5.1", true)]
        [InlineData("1.5.0", "2.0.0", true)]
        public void IsNewer_ComparesSameLengthVersions(string installed, string latest, bool expected)
        {
            Assert.Equal(expected, VersionComparer.IsNewer(installed, latest));
        }

        // A shorter version string (e.g. sqlfluff's own "sqlfluff, version 3.1" CLI output) must
        // compare equal to its zero-padded equivalent, not "older" - the bug System.Version has,
        // since it treats a missing trailing component as -1 instead of 0.
        [Theory]
        [InlineData("3.1", "3.1.0", false)]
        [InlineData("3.1.0", "3.1", false)]
        [InlineData("3.1", "3.1.1", true)]
        [InlineData("3.1.1", "3.1", false)]
        [InlineData("3", "3.0.0", false)]
        public void IsNewer_TreatsMissingTrailingComponentsAsZero(string installed, string latest, bool expected)
        {
            Assert.Equal(expected, VersionComparer.IsNewer(installed, latest));
        }

        [Theory]
        [InlineData(null, "1.6.0")]
        [InlineData("1.5.0", null)]
        [InlineData("not-a-version", "1.6.0")]
        [InlineData("1.5.0", "not-a-version")]
        [InlineData("1.5.0", "3.2.0a1")]
        public void IsNewer_ReturnsFalse_WhenEitherVersionIsUnparsable(string installed, string latest)
        {
            Assert.False(VersionComparer.IsNewer(installed, latest));
        }
    }
}
