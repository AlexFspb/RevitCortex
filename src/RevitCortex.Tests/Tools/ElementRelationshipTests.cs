using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Tools.Elements;
using Xunit;

namespace RevitCortex.Tests.Tools;

public class ElementRelationshipTests
{
    [Fact]
    public void DefaultsAreBoundedAndDependentsAreOptIn()
    {
        var query = ElementRelationshipQuery.Parse(JObject.Parse("{\"elementIds\":[3,1,3,2]}"));
        Assert.Equal(new long[] { 3, 1, 2 }, query.ElementIds);
        Assert.Equal(8, query.MaxDepth); Assert.Equal(50, query.MaxRelated);
        Assert.True(query.IncludeHostChain); Assert.True(query.IncludeSubComponents); Assert.True(query.IncludeInserts);
        Assert.True(query.IncludeAdaptivePoints); Assert.False(query.IncludeDependents);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"elementIds\":[]}")]
    [InlineData("{\"elementIds\":[0]}")]
    [InlineData("{\"elementIds\":[-1]}")]
    [InlineData("{\"elementIds\":[1.0]}")]
    [InlineData("{\"elementIds\":[\"1\"]}")]
    [InlineData("{\"elementIds\":[9223372036854775808]}")]
    [InlineData("{\"elementIds\":[1],\"maxDepth\":17}")]
    [InlineData("{\"elementIds\":[1],\"maxDepth\":0}")]
    [InlineData("{\"elementIds\":[1],\"maxRelated\":101}")]
    [InlineData("{\"elementIds\":[1],\"maxRelated\":0}")]
    [InlineData("{\"elementIds\":[1],\"includeDependents\":\"true\"}")]
    [InlineData("{\"elementIds\":[1],\"includeChildren\":true}")]
    public void InvalidInputsRejectBeforeAnyRevitAccess(string json) =>
        Assert.Throws<ArgumentException>(() => ElementRelationshipQuery.Parse(JObject.Parse(json)));

    [Fact]
    public void IdBudgetAppliesBeforeDeduplicationAndRetainsLongPrecision()
    {
        Assert.Throws<ArgumentException>(() => ElementRelationshipQuery.Parse(new JObject { ["elementIds"] = new JArray(Enumerable.Repeat(1, 101)) }));
        var query = ElementRelationshipQuery.Parse(new JObject { ["elementIds"] = new JArray(9007199254740992L, 9007199254740993L) });
        Assert.Equal(2, query.ElementIds.Length);
    }

    [Fact]
    public void AllFlagsAndLimitBoundariesAreRespected()
    {
        var query = ElementRelationshipQuery.Parse(JObject.Parse("{\"elementIds\":[1],\"includeHostChain\":false,\"includeSubComponents\":false,\"includeInserts\":false,\"includeDependents\":true,\"includeAdaptivePoints\":false,\"maxDepth\":16,\"maxRelated\":100}"));
        Assert.False(query.IncludeHostChain); Assert.False(query.IncludeSubComponents); Assert.False(query.IncludeInserts);
        Assert.True(query.IncludeDependents); Assert.False(query.IncludeAdaptivePoints);
        Assert.Equal(16, query.MaxDepth); Assert.Equal(100, query.MaxRelated);
    }

    [Fact]
    public void HostChainDetectsCycleIncludingStartingElement()
    {
        var calls = 0;
        var chain = ElementRelationshipChain.Walk(1, 8, id => { calls++; return new ElementRelationshipEdge(id == 1 ? 2 : 1, "host", false); });
        Assert.Equal("cycle", chain.Termination); Assert.Single(chain.Edges); Assert.Equal(2, calls);
    }

    [Fact]
    public void HostChainDoesNotTraverseLinkedDocumentIds()
    {
        var calls = 0;
        var chain = ElementRelationshipChain.Walk(1, 8, id => { calls++; return new ElementRelationshipEdge(2, "host", true); });
        Assert.Equal("link_boundary", chain.Termination); Assert.True(chain.Truncated); Assert.Equal(1, calls);
    }

    [Fact]
    public void HostDepthIsStrictlyBoundedAndRootIsDistinguished()
    {
        var calls = 0;
        var chain = ElementRelationshipChain.Walk(1, 3, id => { calls++; return new ElementRelationshipEdge(id + 1, "super_component", false); });
        Assert.Equal("depth_limit", chain.Termination); Assert.Equal(3, calls); Assert.True(chain.Truncated);
        var root = ElementRelationshipChain.Walk(1, 3, _ => null);
        Assert.Equal("root", root.Termination); Assert.Empty(root.Edges); Assert.False(root.Truncated);
    }

    [Fact]
    public void ResponseBudgetIsUtf8BoundedAndEveryRequestedIdGetsAReceipt()
    {
        var output = new ElementRelationshipResults();
        output.Add(1, new JObject { ["elementId"] = 1, ["status"] = "ok", ["name"] = new string('Ж', 300_000) });
        output.Add(2, new JObject { ["elementId"] = 2, ["name"] = new string('Ж', 300_000) });
        for (var id = 3; id <= 100; id++) output.Add(id, null);
        Assert.True(output.Truncated); Assert.Equal(100, output.Records.Count);
        Assert.Equal("ok", output.Records[0]!.Value<string>("status"));
        Assert.Equal("not_read", output.Records[1]!.Value<string>("status"));
        Assert.Equal(100, output.Records[99]!.Value<int>("elementId"));
        Assert.True(Encoding.UTF8.GetByteCount(output.Records.ToString(Formatting.None)) < 1_000_000);
    }
}
