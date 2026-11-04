using Newtonsoft.Json.Linq;
using RevitCortex.Tools.Elements;
using Xunit;

namespace RevitCortex.Tests.Tools;

public class StructuralReviewTests
{
    [Fact]
    public void ExplicitLevelLookupMatchesCaseButRejectsAmbiguousNames()
    {
        Assert.Equal(1, StructuralLevelSelection.NamedIndex(new[] { "Ground", "First" }, "FIRST", "C1"));
        var error = Assert.Throws<ArgumentException>(() =>
            StructuralLevelSelection.NamedIndex(new[] { "Ground", "GROUND" }, "ground", "C1"));
        Assert.Contains("ambiguous", error.Message);
        Assert.Contains("C1", error.Message);
    }

    [Fact]
    public void MissingExplicitLevelIsAnInputError()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            StructuralLevelSelection.NamedIndex(new[] { "Ground" }, "Missing", "B1"));
        Assert.Contains("not found", error.Message);
    }

    [Fact]
    public void LevelChoiceUsesHighestAtOrBelowAndKeepsNegativeOffsetOption()
    {
        Assert.Equal(2, StructuralLevelSelection.AtOrBelowIndex(new[] { 20d, 0d, 10d }, 15));
        Assert.Equal(0, StructuralLevelSelection.AtOrBelowIndex(new[] { 10d, 20d }, 5));
        Assert.Equal(1, StructuralLevelSelection.AtOrBelowIndex(new[] { 10d, 10d, 20d }, 15));
        // A level above the requested elevation does not by itself invalidate a negative offset.
        Assert.Equal(0, StructuralLevelSelection.NamedIndex(new[] { "Upper" }, "upper", "C1"));
    }

    [Fact]
    public void ColumnConstraintReadoutUsesActualValuesWithoutClaimingPhysicalGeometry()
    {
        var data = StructuralColumnConstraintData.Create(9007199254740993L, 9007199254740995L, 10, 20, -2, 3);
        Assert.Equal(9007199254740993L, data.Value<long>("baseLevelId"));
        Assert.Equal(9007199254740995L, data.Value<long>("topLevelId"));
        Assert.Equal(-2d, data.Value<double>("baseOffset_ft"));
        Assert.Equal(8d, data.Value<double>("baseZ_ft"));
        Assert.Equal(23d, data.Value<double>("topZ_ft"));
        Assert.Equal("constraint-derived, not physical geometry", data.Value<string>("basis"));
        Assert.False(data.Value<bool>("physicalZVerified"));
        Assert.Null(data["p0_ft"]);
        Assert.Null(data["p1_ft"]);
    }

    [Fact]
    public void ColumnConstraintsCanUseOneLevelForBothEnds()
    {
        var data = StructuralColumnConstraintData.Create(5, 5, 30, 30, 4, 7);
        Assert.Equal(34d, data.Value<double>("baseZ_ft"));
        Assert.Equal(37d, data.Value<double>("topZ_ft"));
    }

    [Fact]
    public void ColumnReceiptDoesNotPresentConstraintVerificationAsMeasuredPlacement()
    {
        var receipt = new JObject();
        StructuralColumnConstraintData.ApplyVerification(receipt, column: true);
        Assert.False(receipt.Value<bool>("placementVerified"));
        Assert.True(receipt.Value<bool>("constraintsVerified"));
        Assert.False(receipt.Value<bool>("physicalZVerified"));
        Assert.Contains("not verified", receipt.Value<string>("verificationBasis"));
    }

    [Fact]
    public void BeamReceiptStillReportsActualLocationCurveVerification()
    {
        var receipt = new JObject();
        StructuralColumnConstraintData.ApplyVerification(receipt, column: false);
        Assert.True(receipt.Value<bool>("placementVerified"));
        Assert.Equal("actual LocationCurve line endpoints", receipt.Value<string>("verificationBasis"));
        Assert.Null(receipt["constraintsVerified"]);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonfiniteColumnConstraintValuesAreNotSerializedAsGeometry(double invalid)
    {
        Assert.Throws<InvalidOperationException>(() => StructuralColumnConstraintData.Create(1, 2, 0, 10, invalid, 0));
    }

    [Fact]
    public void InvalidColumnLevelIdsAreNotReportedAsValidConstraints()
    {
        Assert.Throws<InvalidOperationException>(() => StructuralColumnConstraintData.Create(-1, 2, 0, 10, 0, 0));
    }
}
