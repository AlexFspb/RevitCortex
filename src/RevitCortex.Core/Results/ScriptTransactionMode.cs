namespace RevitCortex.Core.Results;

public static class ScriptTransactionMode
{
    public static CortexResult<object>? Validate(string? mode) =>
        mode == null || mode == "auto" || mode == "none" || mode == "group" ? null :
        CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "Invalid transactionMode. Supported values: auto, none, group. No script was executed.",
            suggestion: "auto owns one transaction; group owns a transaction group; none owns no transaction. manual and readonly are unsupported. none is not read-only enforcement. Prefer dedicated read-only tools for queries.");
}
