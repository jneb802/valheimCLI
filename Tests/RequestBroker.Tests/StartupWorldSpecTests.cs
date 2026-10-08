using System.IO;
using valheimCLI;
using Xunit;

public sealed class StartupWorldSpecTests
{
    [Theory]
    [InlineData("version=1\nmode=join\ncharacter=vtseed\ntarget=127.0.0.1:2456\npasswordVariable=VT_PASSWORD\n", "join")]
    [InlineData("version=1\nmode=host\ncharacter=vtseed\ntarget=Fixture\npublic=false\ncrossplay=true\n", "host")]
    [InlineData("version=1\nmode=local\ncharacter=vtseed\ntarget=Fixture\ndevcommands=true\n", "local")]
    public void EachModeHasAParseablePasswordFreeSpec(string source, string mode)
    {
        StartupWorldSpec spec = StartupWorldSpec.Parse(source);
        Assert.Equal(mode, spec.Mode);
        Assert.Equal("vtseed", spec.Character);
        Assert.DoesNotContain("password=", source);
        Assert.Equal(mode == "local", spec.Devcommands);
    }

    [Theory]
    [InlineData("version=2\nmode=local\ncharacter=vtseed\ntarget=Fixture\n")]
    [InlineData("version=1\nmode=join\ncharacter=vtseed\ntarget=localhost:2456\npasswordVariable=secret value\n")]
    [InlineData("version=1\nmode=local\ncharacter=vtseed\ntarget=Fixture\npasswordVariable=PW\n")]
    [InlineData("version=1\nmode=join\ncharacter=vtseed\ntarget=localhost:2456\npublic=true\n")]
    [InlineData("version=1\nmode=host\ncharacter=vtseed\ntarget=Fixture\npublic=yes\n")]
    [InlineData("version=1\nmode=local\ncharacter=vtseed\ntarget=Fixture\nmode=join\n")]
    [InlineData("version=1\nmode=local\ncharacter=vtseed\ntarget=Fixture\npassword=secret\n")]
    [InlineData("version=1\nmode=local\ncharacter=../personal\ntarget=Fixture\n")]
    public void InvalidOrSecretBearingSpecsAreRefused(string source) =>
        Assert.Throws<InvalidDataException>(() => StartupWorldSpec.Parse(source));
}
