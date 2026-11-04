using System.Globalization;
using RevitCortex.Tools.MEP;
using Xunit;

namespace RevitCortex.Tests.Tools;

public class MepSizeDiagnosticTests
{
    [Fact]
    public void SizeMismatchIdentifiesParameterAndConvertsActualFeetToMillimeters()
    {
        var message = MepSizeDiagnostic.Message("RBS_PIPE_DIAMETER_PARAM", 47, MepCreationInput.Feet(50),
            "Revit did not retain the requested size");
        Assert.Contains("RBS_PIPE_DIAMETER_PARAM", message);
        Assert.Contains("requestedMm=47", message);
        Assert.Contains("actualMm=50", message);
        Assert.Contains("did not retain", message);
    }

    [Fact]
    public void MissingParameterCannotBeReportedAsZeroSize()
    {
        var message = MepSizeDiagnostic.Message("RBS_CURVE_WIDTH_PARAM", 400, null, "parameter is unavailable");
        Assert.Contains("requestedMm=400", message);
        Assert.Contains("actualMm=unavailable", message);
        Assert.DoesNotContain("actualMm=0", message);
    }

    [Theory]
    [InlineData("parameter is read-only")]
    [InlineData("Parameter.Set returned false")]
    [InlineData("actual size differs after regeneration/commit")]
    public void FailureReasonKeepsRequestedAndActualSizes(string reason)
    {
        var message = MepSizeDiagnostic.Message("RBS_CURVE_DIAMETER_PARAM", 100, 1, reason);
        Assert.Contains(reason, message);
        Assert.Contains("requestedMm=100", message);
        Assert.Contains("actualMm=304.8", message);
    }

    [Fact]
    public void NumericDiagnosticIsInvariantAndPreservesSmallValues()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var message = MepSizeDiagnostic.Message("RBS_PIPE_DIAMETER_PARAM", 47.125, 0.5, "mismatch");
            Assert.Contains("requestedMm=47.125", message);
            Assert.Contains("actualMm=152.4", message);
            Assert.DoesNotContain("47,125", message);
            Assert.Contains("requestedMm=1E-100", MepSizeDiagnostic.Message("diameter", 1e-100, null, "unavailable"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonfiniteActualValuesAreExplicitlyUnavailable(double actualFeet)
    {
        var message = MepSizeDiagnostic.Message("diameter", 50, actualFeet, "invalid actual size");
        Assert.Contains("actualMm=unavailable", message);
    }
}
