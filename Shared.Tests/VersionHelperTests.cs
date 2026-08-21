using System;
using Xunit;

namespace GTAWParser.Shared.Tests
{
    public class VersionHelperTests
    {
        [Theory]
        [InlineData("v6.2.0", "v6.1.0", true)]
        [InlineData("v6.1.0", "v6.2.0", false)]
        [InlineData("v6.2.0", "v6.2.0", false)]
        [InlineData("v6.1.0", "v6.0.0", true)]
        [InlineData("v6.0.0", "v6.1.0", false)]
        [InlineData("6.2.0", "6.1.0", true)]
        [InlineData("v5.0.2", "v5.0.1", true)]
        [InlineData("v5.0.1", "v5.0.2", false)]
        [InlineData("v5.0.2-beta", "v5.0.1", true)]
        [InlineData("v5.0.2-rc1", "v5.0.2", false)]
        [InlineData("v7.0.0.1", "v7.0.0.0", true)]
        public void IsVersionNewer_CorrectlyComparesVersions(string available, string installed, bool expectedNewer)
        {
            bool result = VersionHelper.IsVersionNewer(available, installed);
            Assert.Equal(expectedNewer, result);
        }

        [Theory]
        [InlineData("v5.0.2", 5, 0, 2, 0)]
        [InlineData("5.0.2", 5, 0, 2, 0)]
        [InlineData("v1.2.3.4", 1, 2, 3, 4)]
        [InlineData("v6.1.0-beta", 6, 1, 0, 0)]
        public void TryParseVersion_ParsesValidFormats(string input, int major, int minor, int build, int revision)
        {
            bool success = VersionHelper.TryParseVersion(input, out Version? version);
            Assert.True(success);
            Assert.NotNull(version);
            Assert.Equal(major, version!.Major);
            Assert.Equal(minor, version.Minor);
            Assert.Equal(build, version.Build);
            Assert.Equal(revision, version.Revision);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("invalid")]
        [InlineData("v.a.b")]
        public void TryParseVersion_HandlesInvalidFormats(string? input)
        {
            bool success = VersionHelper.TryParseVersion(input, out Version? version);
            Assert.False(success);
            Assert.Null(version);
        }
    }
}
