using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class ZoneNameParserTests
{
    [Fact]
    public void Parse_StandardPattern_ReturnsStrippedZone()
    {
        ZoneNameParseResult result = ZoneNameParser.Parse("j ラチ内コンコース_床");

        Assert.Equal("ラチ内コンコース", result.ZoneName);
        Assert.True(result.PatternMatched);
    }

    [Fact]
    public void Parse_NoSuffix_ReturnsTrimmedName()
    {
        ZoneNameParseResult result = ZoneNameParser.Parse("SomeOtherType");

        Assert.Equal("SomeOtherType", result.ZoneName);
        Assert.False(result.PatternMatched);
    }

    [Fact]
    public void Parse_PrefixOnly_DoesNotStrip()
    {
        ZoneNameParseResult result = ZoneNameParser.Parse("j ラチ内コンコース");

        Assert.Equal("j ラチ内コンコース", result.ZoneName);
        Assert.False(result.PatternMatched);
    }

    [Fact]
    public void Parse_WithParentheses_PreservesInnerText()
    {
        ZoneNameParseResult result = ZoneNameParser.Parse("j ラチ内コンコース(JR東日本新幹線)_床");

        Assert.Equal("ラチ内コンコース(JR東日本新幹線)", result.ZoneName);
        Assert.True(result.PatternMatched);
    }
}
