using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware;

public class VictusFanTachometerTests
{
    [Theory]
    [InlineData("8C2F", "F.08", true)]
    [InlineData("8c2f", "f.08", true)]
    [InlineData("8C2F", "F.09", false)]
    [InlineData("8BBE", "F.08", false)]
    [InlineData(null, "F.08", false)]
    [InlineData("8C2F", null, false)]
    public void TachometerPathRequiresExactBoardAndFirmware(string? board, string? bios, bool expected)
    {
        Assert.Equal(expected, HpWmiBios.MatchesVictusFanTachometerFirmware(board, bios));
    }

    [Fact]
    public void DecodesEachChannelAndPreservesRealZero()
    {
        Assert.Equal((3400, 2410), HpWmiBios.ParseVictusFanTachometers([0, 0, 0x0d, 0x48], [0, 0, 0x09, 0x6a]));
        Assert.Equal((0, 2410), HpWmiBios.ParseVictusFanTachometers([0, 0, 0, 0], [0, 0, 0x09, 0x6a]));
    }

    [Fact]
    public void RejectsMissingMalformedAndImplausiblePayloads()
    {
        byte[] valid = [0, 0, 0x0d, 0x48];
        Assert.Null(HpWmiBios.ParseVictusFanTachometers(null, valid));
        Assert.Null(HpWmiBios.ParseVictusFanTachometers(valid, [0, 0, 0]));
        Assert.Null(HpWmiBios.ParseVictusFanTachometers(valid, [0, 0, 0xff, 0xff]));
    }
}
