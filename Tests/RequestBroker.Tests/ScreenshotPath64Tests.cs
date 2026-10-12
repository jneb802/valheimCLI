using System;
using System.IO;
using System.Text;
using Xunit;

namespace valheimCLI.Tests
{
    public class ScreenshotPath64Tests
    {
        [Fact]
        public void DecodesOneAbsolutePathWithSpaces()
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "owned screenshots", "one.png"));
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.True(ScreenshotPath64.TryDecode(token, out string decoded));
            Assert.Equal(path, decoded);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("../elsewhere.png")]
        [InlineData("bad+base64")]
        public void RejectsMalformedOrNonAbsoluteTokens(string token)
        {
            Assert.False(ScreenshotPath64.TryDecode(token, out _));
        }
    }
}
