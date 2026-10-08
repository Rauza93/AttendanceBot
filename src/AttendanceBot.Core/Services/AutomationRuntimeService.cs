using AttendanceBot.Core.Models;

namespace AttendanceBot.Core.Services;

/// <summary>Single process-wide source of truth for operator-visible automation state.</summary>
public sealed class AutomationRuntimeService
{
    private readonly object _gate = new();
    private AutomationSnapshot _snapshot = new(AutomationState.Stopped);
    private CancellationTokenSource? _runCancellation;

    public AutomationSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public event EventHandler<AutomationSnapshot>? StateChanged;
    public CancellationToken RunToken { get { lock (_gate) return _runCancellation?.Token ?? CancellationToken.None; } }

    public bool Start()
    {
        lock (_gate)
        {
            if (_snapshot.State is AutomationState.Running or AutomationState.Processing) return false;
            _runCancellation?.Dispose(); _runCancellation = new CancellationTokenSource();
            SetLocked(new(AutomationState.Running, Mode: "Waiting for schedule"));
            return true;
        }
    }

    public bool Pause()
    {
        lock (_gate)
        {
            if (_snapshot.State != AutomationState.Running) return false;
            SetLocked(_snapshot with { State = AutomationState.Paused, Mode = "Paused" }); return true;
        }
    }

    public bool Resume()
    {
        lock (_gate)
        {
            if (_snapshot.State != AutomationState.Paused) return false;
            SetLocked(_snapshot with { State = AutomationState.Running, Mode = "Waiting for schedule" }); return true;
        }
    }

    public bool Stop()
    {
        lock (_gate)
        {
            if (_snapshot.State == AutomationState.Stopped) return false;
            _runCancellation?.Cancel();
            SetLocked(new(AutomationState.Stopped)); return true;
        }
    }

    public void SetProcessing(string profile, string provider, AttendanceType action, int queueRemaining) =>
        Update(new(AutomationState.Processing, profile, provider, action, queueRemaining, "Processing", StartedAt: DateTimeOffset.Now));

    public void SetWaiting(DateTimeOffset? nextSchedule = null) =>
        Update(new(AutomationState.Running, Mode: "Waiting for schedule", NextSchedule: nextSchedule));

    public void SetNextSchedule(DateTimeOffset? nextSchedule) => Update(_snapshot with { NextSchedule = nextSchedule });

    public void SetError(string error) => Update(new(AutomationState.Error, Mode: "Error", Error: error));

    private void Update(AutomationSnapshot snapshot) { lock (_gate) SetLocked(snapshot); }
    private void SetLocked(AutomationSnapshot snapshot)
    {
        _snapshot = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }
}
