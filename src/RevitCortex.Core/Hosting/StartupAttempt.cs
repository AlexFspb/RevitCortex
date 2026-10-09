namespace RevitCortex.Core.Hosting;
public sealed class StartupAttempt
{
    private bool _consumed;
    public bool Take() { if (_consumed) return false; _consumed = true; return true; }
    public void Suppress() => _consumed = true;
}
