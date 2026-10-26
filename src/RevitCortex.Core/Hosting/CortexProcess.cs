namespace RevitCortex.Core.Hosting;

/// <summary>Core targets netstandard2.0: cache the PID and dispose the Process wrapper once.</summary>
public static class CortexProcess
{
    public static int Id { get; } = ReadId();
    private static int ReadId()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.Id;
    }
}
