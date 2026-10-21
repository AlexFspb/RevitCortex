using System;
using System.Collections.Generic;

namespace RevitCortex.Core.Results;

/// <summary>A blocked API operation is distinct from cancellation and from confirmed rollback.</summary>
public sealed class ScriptPreconditionException : Exception
{
    public string Reason { get; }
    public string Operation { get; }

    public ScriptPreconditionException(string reason, string operation, string message) : base(message)
    {
        Reason = reason;
        Operation = operation;
    }

    public CortexResult<object> ToFailure(bool scriptExecuted) => CortexResult<object>.Fail(
        CortexErrorCode.ScriptPreconditionFailed,
        Message + (scriptExecuted ? " The blocked operation was not called; earlier script effects may remain."
            : " No script code was executed. Cortex did not close or roll back any existing transaction."),
        suggestion: "Inspect document and transaction state before retrying. Use transactionMode=none for EditFamily and finish the script's own transactions before that call. Never close another operation's transaction or retry blindly.",
        context: new Dictionary<string, object>
        {
            ["reason"] = Reason, ["blockedOperation"] = Operation,
            ["phase"] = scriptExecuted ? "before_api_call" : "before_script",
            ["scriptExecuted"] = scriptExecuted,
            ["transactionState"] = scriptExecuted ? "not_managed" : "not_started_by_cortex",
            ["externalEffectsMayRemain"] = scriptExecuted
        });
}
