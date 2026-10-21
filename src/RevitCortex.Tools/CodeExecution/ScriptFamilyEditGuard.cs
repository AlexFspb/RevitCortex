using Autodesk.Revit.DB;
using RevitCortex.Core.Results;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>UI-thread checks. Does not end transactions, approve dialogs or retry API operations.</summary>
public static class ScriptFamilyEditGuard
{
    public static CortexResult<object>? CheckStart(Document document, string transactionMode)
    {
        var error = CheckDocument(document, "script_start", requireWritable: transactionMode != "none");
        return error?.ToFailure(scriptExecuted: false);
    }

    // Roslyn rewrites supported direct Document.EditFamily calls to this method.
    // Parameter name preserves named-argument calls from the original API.
    public static Document EditFamily(Document document, Family loadedFamily)
    {
        var error = CheckDocument(document, "Document.EditFamily", requireWritable: true);
        if (error != null) throw error;
        if (loadedFamily == null || !loadedFamily.IsValidObject)
            throw Block("InvalidFamily", "The family no longer exists.");
        if (!document.Equals(loadedFamily.Document))
            throw Block("FamilyDocumentMismatch", "The family does not belong to the supplied document.");
        if (loadedFamily.IsInPlace || !loadedFamily.IsEditable)
            throw Block("FamilyNotEditable", "The family is in-place or does not support EditFamily.");

        // Revit still enforces remaining native preconditions, e.g. dynamic update/already editing.
        return document.EditFamily(loadedFamily);
    }

    private static ScriptPreconditionException? CheckDocument(Document document, string operation, bool requireWritable)
    {
        if (document == null || !document.IsValidObject)
            return new ScriptPreconditionException("InvalidDocument", operation, "The document is missing or no longer valid.");
        if (document.IsModifiable)
            return new ScriptPreconditionException("OpenTransaction", operation,
                "Cortex blocked the operation because the document has an open transaction (IsModifiable=true).");
        if (requireWritable && document.IsReadOnly)
            return new ScriptPreconditionException("ReadOnlyDocument", operation, "The document is currently read-only.");
        return null;
    }

    private static ScriptPreconditionException Block(string reason, string message) =>
        new(reason, "Document.EditFamily", message);
}
