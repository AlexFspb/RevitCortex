using System;
using RevitCortex.Core.Results;
namespace RevitCortex.Core.Session;
public sealed class DocumentContextChangedException : Exception
{
    public DocumentContextChangedException() : base("The selected Revit instance or active document changed. No new approval was granted. Verify the intended target with get_project_info or say_hello before a new request; do not retry blindly.") { }
    public CortexResult<object> ToResult() => CortexResult<object>.Fail(CortexErrorCode.Cancelled, Message);
}
