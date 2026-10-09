namespace RevitCortex.Core.Session;

/// <summary>One ordinary-operation decision; independent of the critical C# policy.</summary>
public sealed class OperationApprovalCountdown
{
    public const int DelaySeconds = 3;
    public bool AutoRunEnabled { get; private set; }
    public int SecondsRemaining { get; private set; } = DelaySeconds;
    public bool? Decision { get; private set; }
    private bool _started;

    public OperationApprovalCountdown(bool autoRunEnabled = true)
    {
        AutoRunEnabled = autoRunEnabled;
    }

    // Called only once the dialog is visible. Construction cannot approve anything.
    public void Start() => _started = true;

    public void SetAutoRun(bool enabled)
    {
        if (Decision.HasValue) return;
        AutoRunEnabled = enabled;
        SecondsRemaining = DelaySeconds;
    }

    public void Tick()
    {
        if (!_started || !AutoRunEnabled || Decision.HasValue) return;
        if (--SecondsRemaining <= 0) ApproveOnce();
    }

    public void ApproveOnce()
    {
        if (!Decision.HasValue) Decision = true;
    }

    public void Cancel()
    {
        if (!Decision.HasValue) Decision = false;
    }
}
