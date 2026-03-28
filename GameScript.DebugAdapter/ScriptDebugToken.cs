using System.Threading;

namespace GameScript.DebugAdapter;

/// <summary>
/// Per-script pause/resume state. Thread-safe between the game thread (which runs
/// the script) and the DAP network thread (which handles debugger requests).
/// </summary>
public sealed class ScriptDebugToken
{
    private volatile PauseReason _pendingPause;
    private volatile StepContext? _stepContext;
    private readonly SemaphoreSlim _pauseGate = new(0, 1);

    public bool IsDisconnected { get; private set; }
    public bool IsStepping => _stepContext != null;

    /// <summary>
    /// Called by the DAP thread to request a pause at the next instruction.
    /// </summary>
    public void RequestPause(PauseReason reason)
    {
        _stepContext = null;
        _pendingPause = reason;
    }

    /// <summary>
    /// Called by the DAP thread to set a step and resume execution.
    /// </summary>
    public void Step(StepContext context)
    {
        _pendingPause = PauseReason.None;
        _stepContext = context;
        _pauseGate.Release();
    }

    /// <summary>
    /// Called by the DAP thread to resume free execution.
    /// </summary>
    public void Resume()
    {
        _pendingPause = PauseReason.None;
        _stepContext = null;
        _pauseGate.Release();
    }

    /// <summary>
    /// Called by the DAP thread on disconnect. Unblocks any waiting game thread.
    /// </summary>
    public void Disconnect()
    {
        IsDisconnected = true;
        _pauseGate.Release();
    }

    /// <summary>
    /// Called by the game thread after each instruction.
    /// Returns true if the game thread should pause now.
    /// <paramref name="currentLine"/> is the source line for the current instruction (-1 if unknown).
    /// </summary>
    public bool CheckAndPause(int currentFrameDepth, int currentLine, out PauseReason reason)
    {
        // Check explicit pause request
        var pending = _pendingPause;
        if (pending != PauseReason.None)
        {
            _pendingPause = PauseReason.None;
            reason = pending;
            return true;
        }

        // Check step completion — only pause on a new line so multiple ops on the same
        // source line don't produce repeated stops.
        var step = _stepContext;
        if (step != null)
        {
            var shouldPause = step.Kind switch
            {
                // Step-in: pause when we enter a deeper frame OR land on a different line.
                StepKind.In  => currentFrameDepth > step.BaseFrameDepth
                             || (currentLine >= 0 && currentLine != step.BaseLine),

                // Step-over: same/shallower depth AND a new line.
                StepKind.Over => currentFrameDepth <= step.BaseFrameDepth
                              && (currentLine >= 0 && currentLine != step.BaseLine),

                // Step-out: just need to return to a shallower frame.
                StepKind.Out  => currentFrameDepth < step.BaseFrameDepth,

                _ => false,
            };
            if (shouldPause)
            {
                _stepContext = null;
                reason = PauseReason.Step;
                return true;
            }
        }

        reason = default;
        return false;
    }

    /// <summary>
    /// Called by the game thread to block until the DAP thread resumes or disconnects.
    /// </summary>
    public void WaitForResume() => _pauseGate.Wait();
}
