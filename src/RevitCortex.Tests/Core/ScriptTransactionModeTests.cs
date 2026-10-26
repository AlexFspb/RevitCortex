using RevitCortex.Core.Results;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Core;

public class ScriptTransactionModeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("auto")]
    [InlineData("none")]
    [InlineData("group")]
    public void AcceptsOnlyImplementedModes(string? mode) => Assert.Null(ScriptTransactionMode.Validate(mode));

    [Theory]
    [InlineData("none")]
    [InlineData("group")]
    public async Task StrictWarningsCannotSilentlyLeaveOwnedTransactionsUnprotected(string mode)
    {
        Assert.Null(ScriptTransactionMode.Validate(mode, false));
        var response = await ProjectTools.SendCodeToRevit(null!, "return 1;", mode, strictWarnings: true);
        Assert.Contains("InvalidInput", response);
        Assert.Contains("Configure", response);
        Assert.Contains("No script was executed", response);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("auto")]
    public void StrictWarningsSupportsDefaultAuto(string? mode) =>
        Assert.Null(ScriptTransactionMode.Validate(mode, true));

    [Theory]
    [InlineData("readonly")]
    [InlineData("manual")]
    [InlineData("AUTO")]
    [InlineData("")]
    [InlineData("typo")]
    public async Task InvalidModeFailsBeforeAnyBridgeConnection(string mode)
    {
        Assert.Equal(CortexErrorCode.InvalidInput, ScriptTransactionMode.Validate(mode)!.Error!.Code);
        // No connection manager: attempting bridge I/O would fail this test.
        var response = await ProjectTools.SendCodeToRevit(null!, "return 1;", mode);
        Assert.Contains("InvalidInput", response);
        Assert.Contains("No script was executed", response);
    }
}
