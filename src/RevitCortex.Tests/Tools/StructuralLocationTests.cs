using Newtonsoft.Json.Linq;
using RevitCortex.Tools.Elements;
using Xunit;

namespace RevitCortex.Tests.Tools;

public class StructuralLocationTests
{
    [Fact]
    public void DefaultsAreBoundedAndIncludeBothCategories()
    {
        Assert.True(StructuralLocationQuery.TryParse(new JObject(), out var query, out _));
        Assert.Equal(new[] { "StructuralFraming", "StructuralColumns" }, query.Categories);
        Assert.Null(query.ElementIds);
        Assert.Equal(500, query.Limit);
        Assert.False(query.IncludeTypeParams);
    }

    [Theory]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":501}")]
    [InlineData("{\"limit\":\"5\"}")]
    [InlineData("{\"offset\":-1}")]
    [InlineData("{\"offset\":2147483648}")]
    [InlineData("{\"elementIds\":[1.5]}")]
    [InlineData("{\"elementIds\":[\"12\"]}")]
    [InlineData("{\"elementIds\":[0]}")]
    [InlineData("{\"elementIds\":[9223372036854775808]}")]
    [InlineData("{\"includeTypeParams\":1}")]
    [InlineData("{\"categories\":[\"Walls\"]}")]
    [InlineData("{\"categories\":[]}")]
    public void RejectsInvalidInputsWithoutCoercion(string json)
    {
        Assert.False(StructuralLocationQuery.TryParse(JObject.Parse(json), out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void IdsHaveStableOrderingAndEmptyArrayIsExplicitEmptyRequest()
    {
        Assert.True(StructuralLocationQuery.TryParse(JObject.Parse("{\"elementIds\":[30,2,30],\"categories\":[\"structuralcolumns\"]}"), out var query, out _));
        Assert.Equal(new long[] { 2, 30 }, query.ElementIds);
        Assert.Equal(new[] { "StructuralColumns" }, query.Categories);
        Assert.True(StructuralLocationQuery.TryParse(JObject.Parse("{\"elementIds\":[]}"), out query, out _));
        Assert.NotNull(query.ElementIds);
        Assert.Empty(query.ElementIds);
    }

    [Fact]
    public void RequestedIdsBudgetAppliesBeforeDeduplication()
    {
        var input = new JObject { ["elementIds"] = new JArray(Enumerable.Repeat(1, 5001)) };
        Assert.False(StructuralLocationQuery.TryParse(input, out _, out _));
    }

    [Fact]
    public void PointDoesNotInventAnAxis()
    {
        var point = StructuralLocationData.Point(new[] { 1d, 2d, 3d });
        Assert.False((bool)point["axisIsExact"]!);
        Assert.False((bool)point["canReconstruct"]!);
        Assert.Null(point["p0_ft"]);
        Assert.Null(point["p1_ft"]);
        Assert.Equal(3d, (double)point["point_ft"]![2]!);
    }

    [Fact]
    public void ArcIncludesThirdPointAndPlaneInsteadOfPretendingToBeALine()
    {
        var arc = StructuralLocationData.Arc(new[] { 1d, 0d, 0d }, new[] { -1d, 0d, 0d },
            new[] { 0d, 1d, 0d }, new[] { 0d, 0d, 0d }, 1d, new[] { 0d, 0d, 1d });
        Assert.Equal("arc", (string?)arc["curveType"]);
        Assert.Equal(1d, (double)arc["midpoint_ft"]![1]!);
        Assert.Equal(1d, (double)arc["radius_ft"]!);
        Assert.Equal(1d, (double)arc["normal"]![2]!);
    }

    [Fact]
    public void UnsupportedLocationCannotBeReconstructedFromExtentCorners()
    {
        var geometry = StructuralLocationData.Unsupported("NurbSpline");
        Assert.False((bool)geometry["canReconstruct"]!);
        Assert.Null(geometry["p0_ft"]);
        Assert.Null(geometry["p1_ft"]);
    }
}
