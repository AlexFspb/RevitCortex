using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
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

    [Fact]
    public void OversizedFirstUtf8RowBecomesDiagnosticAndAdvancesCursor()
    {
        var row = new JObject { ["elementId"] = 7, ["text"] = new string('Ж', 600_000) };
        // Character count is below the byte budget, but its UTF-8 representation is not.
        Assert.True(row.ToString(Formatting.None).Length < StructuralLocationQuery.ResponseBudgetBytes);
        var page = new StructuralLocationPage(500);
        Assert.True(page.TryAdd(7, row, null));
        Assert.Empty(page.Elements);
        Assert.Equal(1, page.ProcessedCount);
        Assert.Equal(101, page.NextOffset(100));
        Assert.Equal("RowExceedsBudget", (string?)page.Diagnostics[0]!["code"]);
        Assert.Equal(7L, (long)page.Diagnostics[0]!["elementId"]!);
        Assert.True(Encoding.UTF8.GetByteCount(page.Diagnostics.ToString(Formatting.None)) < 1000);
    }

    [Fact]
    public void LaterOversizedRowStopsCurrentPageAndMakesProgressOnNextPage()
    {
        var large = new JObject { ["elementId"] = 2, ["text"] = new string('x', 1_000_000) };
        var first = new StructuralLocationPage(500);
        Assert.True(first.TryAdd(1, new JObject { ["elementId"] = 1 }, null));
        Assert.False(first.TryAdd(2, large, null));
        Assert.Single(first.Elements);
        Assert.Equal(1, first.NextOffset(0));

        var second = new StructuralLocationPage(500);
        Assert.True(second.TryAdd(2, large, null));
        Assert.Equal(2, second.NextOffset(1));
        Assert.Equal("RowExceedsBudget", (string?)second.Diagnostics[0]!["code"]);
    }

    [Fact]
    public void PageLimitCountsDiagnosticsAndUses64BitCursorArithmetic()
    {
        var page = new StructuralLocationPage(1);
        Assert.True(page.TryAdd(5, null, new JObject { ["elementId"] = 5, ["code"] = "ElementNotFound" }));
        Assert.False(page.TryAdd(6, new JObject { ["elementId"] = 6 }, null));
        Assert.Equal(1, page.ProcessedCount);
        Assert.Equal(2147483648L, page.NextOffset(int.MaxValue));
        Assert.Single(page.Diagnostics);
    }
}
