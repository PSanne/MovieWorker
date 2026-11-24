using MovieWorker.Core.Time;

namespace MovieWorker.Core.Tests;

public class TimeTests
{
    [Theory]
    [InlineData("30000/1001", 30000, 1001)]
    [InlineData("25/1", 25, 1)]
    [InlineData("50", 50, 1)]
    [InlineData("29.97", 30000, 1001)]
    [InlineData("23.976", 24000, 1001)]
    [InlineData("50/2", 25, 1)]
    public void FrameRate_Parse(string text, int num, int den)
    {
        var rate = FrameRate.Parse(text);
        Assert.Equal(num, rate.Numerator);
        Assert.Equal(den, rate.Denominator);
    }

    [Theory]
    [InlineData("0/0")]
    [InlineData("abc")]
    [InlineData("")]
    public void FrameRate_TryParse_RejectsInvalid(string text) => Assert.False(FrameRate.TryParse(text, out _));

    [Fact]
    public void FrameRate_FrameSecondConversion_IsExact()
    {
        var rate = FrameRate.Fps29_97;
        Assert.Equal(1001.0, rate.FramesToSeconds(30000), 9);
        Assert.Equal(30000, rate.SecondsToFrames(1001.0));
    }

    [Theory]
    [InlineData(0, "00:00:00:00")]
    [InlineData(25, "00:00:01:00")]
    [InlineData(25 * 3661 + 7, "01:01:01:07")]
    public void Timecode_NonDrop_25(long frames, string expected) =>
        Assert.Equal(expected, Timecode.FromFrames(frames, FrameRate.Fps25));

    [Theory]
    [InlineData(1799, "00:00:59;29")]
    [InlineData(1800, "00:01:00;02")]
    [InlineData(17982, "00:10:00;00")]
    [InlineData(107892, "01:00:00;00")]
    public void Timecode_DropFrame_2997(long frames, string expected)
    {
        Assert.Equal(expected, Timecode.FromFrames(frames, FrameRate.Fps29_97, dropFrame: true));
        Assert.Equal(frames, Timecode.ToFrames(expected, FrameRate.Fps29_97));
    }

    [Fact]
    public void Timecode_RoundTrip_NonDrop() =>
        Assert.Equal(123456, Timecode.ToFrames(Timecode.FromFrames(123456, FrameRate.Fps50), FrameRate.Fps50));

    [Fact]
    public void Timecode_DropFrameIgnoredFor25() =>
        Assert.Equal("00:01:00:00", Timecode.FromFrames(1500, FrameRate.Fps25, dropFrame: true));
}
