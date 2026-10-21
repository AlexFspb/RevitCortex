using System;

namespace RevitCortex.Core.Session;

/// <summary>One host instance's user-controlled approval policy. Never persisted or set by scripts.</summary>
public sealed class AutomaticExecutionPolicy
{
    public bool Enabled { get; private set; } = true;
    public void SetEnabled(bool enabled) => Enabled = enabled;

    public bool? Decide(Func<bool?> manualConfirmation) => Enabled ? true : manualConfirmation();
}
