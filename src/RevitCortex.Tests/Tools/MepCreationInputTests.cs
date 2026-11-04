using Newtonsoft.Json.Linq;
using RevitCortex.Tools.MEP;
using Xunit;

namespace RevitCortex.Tests.Tools;

public class MepCreationInputTests
{
    private static JObject PipeInput() => new()
    {
        ["systemTypeId"] = 11, ["pipeTypeId"] = 12, ["levelId"] = 13,
        ["startPoint"] = new JArray(0, 0, 0), ["endPoint"] = new JArray(3048, 0, 0), ["diameterMm"] = 25
    };
    private static JObject DuctInput(string shape = "rectangular")
    {
        var input = PipeInput(); input.Remove("pipeTypeId"); input["ductTypeId"] = 22; input["shape"] = shape;
        if (shape != "round") { input.Remove("diameterMm"); input["widthMm"] = 400; input["heightMm"] = 200; }
        return input;
    }

    [Fact]
    public void PipePreviewDefaultsAndMillimeterConversionAreExplicit()
    {
        var input = PipeInput(); var before = input.ToString();
        var result = MepCreationInput.Parse(input, true);
        Assert.True(result.IsPipe); Assert.True(result.DryRun); Assert.Equal("round", result.Shape);
        Assert.Equal(3048, result.LengthMm); Assert.Equal(10, MepCreationInput.Feet(result.LengthMm));
        Assert.Equal(25, result.DiameterMm); Assert.Equal(before, input.ToString());
    }

    [Theory]
    [InlineData("round")]
    [InlineData("rectangular")]
    [InlineData("oval")]
    public void DuctAcceptsOnlyItsDeclaredShapeDimensions(string shape)
    {
        var result = MepCreationInput.Parse(DuctInput(shape), false);
        Assert.False(result.IsPipe); Assert.Equal(shape, result.Shape); Assert.True(result.DryRun);
        Assert.Equal(shape == "round", result.DiameterMm.HasValue);
        Assert.Equal(shape != "round", result.WidthMm.HasValue && result.HeightMm.HasValue);
    }

    [Fact]
    public void DuctShapeIsRequiredAndUnsupportedShapesFail()
    {
        var missing = DuctInput(); missing.Remove("shape");
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(missing, false));
        var unsupported = DuctInput(); unsupported["shape"] = "flex";
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(unsupported, false));
    }

    [Fact]
    public void ShapeMismatchDoesNotSilentlyIgnoreExtraDimensions()
    {
        var round = DuctInput("round"); round["widthMm"] = 100;
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(round, false));
        var rect = DuctInput(); rect["diameterMm"] = 100;
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(rect, false));
        rect.Remove("diameterMm"); rect.Remove("heightMm");
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(rect, false));
        var pipe = PipeInput(); pipe["widthMm"] = 100;
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(pipe, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1000001)]
    public void InvalidSizeCannotReachNativeApi(double diameter)
    {
        var input = PipeInput(); input["diameterMm"] = diameter;
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
    }

    [Theory]
    [InlineData("systemTypeId")]
    [InlineData("pipeTypeId")]
    [InlineData("levelId")]
    public void ExplicitPositiveIdsAreRequiredWithoutCoercion(string name)
    {
        foreach (var value in new JToken[] { JValue.CreateNull(), new JValue(0), new JValue(-1), new JValue("11"), new JValue(11.0) })
        {
            var input = PipeInput(); input[name] = value;
            Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
        }
        var absent = PipeInput(); absent.Remove(name);
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(absent, true));
    }

    [Fact]
    public void NativeMinimumLengthIsNotJustCoincidenceCheck()
    {
        foreach (var mm in new[] { 0, 0.1, 2.53 })
        {
            var input = PipeInput(); input["endPoint"] = new JArray(mm, 0, 0);
            Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
        }
        var accepted = PipeInput(); accepted["endPoint"] = new JArray(2.54, 0, 0);
        Assert.Equal(2.54, MepCreationInput.Parse(accepted, true).LengthMm);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e10)]
    public void NonfiniteOrHugeCoordinatesAreRejected(double coordinate)
    {
        var input = PipeInput(); input["startPoint"] = new JArray(coordinate, 0, 0);
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
    }

    [Fact]
    public void TextCoordinatesAndWrongArrayLengthAreRejected()
    {
        var input = PipeInput(); input["startPoint"] = new JArray("0", 0, 0);
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
        input["startPoint"] = new JArray(0, 0);
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
    }

    [Fact]
    public void HostContextAndExplicitWriteAreAcceptedButTextApprovalIsNot()
    {
        var input = PipeInput(); input["dryRun"] = false;
        input["_cortexExpected"] = new JObject { ["instanceId"] = "verified-host" };
        Assert.False(MepCreationInput.Parse(input, true).DryRun);
        input["dryRun"] = "false";
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
    }

    [Fact]
    public void CoordinateSystemFallbackAndNameBasedSelectionAreNotAccepted()
    {
        foreach (var name in new[] { "pipeType", "systemType", "levelName", "sharedCoordinates", "connectToId" })
        {
            var input = PipeInput(); input[name] = "fallback";
            Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
        }
    }

    [Fact]
    public void Positive64BitIdsRetainPrecision()
    {
        var input = PipeInput(); input["pipeTypeId"] = 9007199254740993L;
        Assert.Equal(9007199254740993L, MepCreationInput.Parse(input, true).TypeId);
        input["pipeTypeId"] = JToken.Parse("9223372036854775808");
        Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
    }

    [Theory]
    [InlineData("9223372036854775808")]
    [InlineData("-9223372036854775809")]
    public void BigIntegerCoordinatesAndDimensionsAreInputErrors(string jsonNumber)
    {
        foreach (var dimension in new[] { false, true })
        {
            var input = PipeInput();
            if (dimension) input["diameterMm"] = JToken.Parse(jsonNumber);
            else input["startPoint"] = new JArray(JToken.Parse(jsonNumber), 0, 0);
            var error = Assert.Throws<ArgumentException>(() => MepCreationInput.Parse(input, true));
            Assert.Contains("64-bit", error.Message);
        }
    }
}
