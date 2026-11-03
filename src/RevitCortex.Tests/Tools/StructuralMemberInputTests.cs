using Newtonsoft.Json.Linq;
using RevitCortex.Tools.Elements;
using Xunit;

namespace RevitCortex.Tests.Tools;

public class StructuralMemberInputTests
{
    private static JObject Member(string id = "beam-1") => new()
    {
        ["sourceId"] = id, ["role"] = "beam", ["family"] = "Beam family", ["baseType"] = "Base",
        ["typeName"] = "Requested", ["p0_ft"] = new JArray(0, 0, 0), ["p1_ft"] = new JArray(10, 0, 0)
    };
    private static JObject Request(params JObject[] members) => new() { ["members"] = new JArray(members) };

    [Fact]
    public void DefaultsArePreviewAndUnpinned_WithoutChangingInput()
    {
        var json = Request(Member()); var before = json.ToString();
        var parsed = StructuralMemberInput.Parse(json);
        Assert.True(parsed.DryRun); Assert.False(parsed.Pin); Assert.Single(parsed.Members);
        Assert.Equal(before, json.ToString());
    }

    [Fact]
    public void ValidExplicitWriteAndHostContextAreAccepted()
    {
        var json = Request(Member()); json["dryRun"] = false; json["pin"] = true;
        json["_cortexExpected"] = new JObject { ["instanceId"] = "instance" };
        var parsed = StructuralMemberInput.Parse(json);
        Assert.False(parsed.DryRun); Assert.True(parsed.Pin);
    }

    [Theory]
    [InlineData("dryRun", "false")]
    [InlineData("pin", "true")]
    public void BooleanFlagsDoNotCoerceStrings(string field, string value)
    {
        var json = Request(Member()); json[field] = value;
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void BatchSizeIsBounded(int count)
    {
        var json = Request(Enumerable.Range(0, count).Select(i => Member(i.ToString())).ToArray());
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(json));
    }

    [Fact]
    public void ReusedSourceIdAndConflictingTypeRequestsRejectWholeBatch()
    {
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(Member(), Member())));
        var a = Member("a"); var b = Member("b");
        a["typeParams"] = new JObject { ["Width"] = 1.0 }; b["typeParams"] = new JObject { ["Width"] = 2.0 };
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(a, b)));
    }

    [Fact]
    public void MatchingRepeatedTypesPermitDifferentInstancesAndNumericTokenForms()
    {
        var a = Member("a"); var b = Member("b");
        a["typeParams"] = new JObject { ["Width"] = 1 }; b["typeParams"] = new JObject { ["Width"] = 1.0 };
        b["instanceParams"] = new JObject { ["Comments"] = "Other instance" };
        Assert.Equal(2, StructuralMemberInput.Parse(Request(a, b)).Members.Count);
    }

    [Fact]
    public void Distinct64BitElementIdParametersCannotCollapseThroughDoublePrecision()
    {
        var a = Member("a"); var b = Member("b");
        a["typeParams"] = new JObject { ["Material"] = 9007199254740992L };
        b["typeParams"] = new JObject { ["Material"] = 9007199254740993L };
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(a, b)));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public void SlantedColumnsAreRejectedBeforeAnyApiUse(double x, double y)
    {
        var member = Member(); member["role"] = "column"; member["p1_ft"] = new JArray(x, y, 10);
        var error = Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(member)));
        Assert.Contains("vertical", error.Message);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    public void VerticalColumnsPermitEitherEndpointOrder(double z0, double z1)
    {
        var member = Member(); member["role"] = "column";
        member["p0_ft"] = new JArray(1, 2, z0); member["p1_ft"] = new JArray(1, 2, z1);
        Assert.Single(StructuralMemberInput.Parse(Request(member)).Members);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e8)]
    public void InvalidCoordinatesAreRejected(double coordinate)
    {
        var member = Member(); member["p1_ft"] = new JArray(coordinate, 0, 0);
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(member)));
    }

    [Fact]
    public void CoincidentEndpointsAreRejected()
    {
        var member = Member(); member["p1_ft"] = member["p0_ft"]!.DeepClone();
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(member)));
    }

    [Theory]
    [InlineData("family")]
    [InlineData("baseType")]
    [InlineData("typeName")]
    [InlineData("sourceId")]
    public void MissingNamesAreRejected(string field)
    {
        var member = Member(); member.Remove(field);
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(member)));
    }

    [Fact]
    public void UnknownGeometryFieldCannotBeSilentlyIgnored()
    {
        var member = Member(); member["rotation"] = 90;
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(member)));
    }

    [Fact]
    public void ParametersMustBeBoundedPrimitiveValues()
    {
        var member = Member(); member["instanceParams"] = new JObject { ["Width"] = new JArray(1, 2) };
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(member)));
        member["instanceParams"] = new JObject(Enumerable.Range(0, 33).Select(i => new JProperty("p" + i, i)));
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(member)));
        member["instanceParams"] = new JObject { ["Comments"] = new string('x', 1025) };
        Assert.Throws<ArgumentException>(() => StructuralMemberInput.Parse(Request(member)));
    }
}
