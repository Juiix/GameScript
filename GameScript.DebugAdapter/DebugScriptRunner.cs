using System;
using GameScript.Bytecode;

namespace GameScript.DebugAdapter;

/// <summary>
/// Drop-in replacement for <see cref="ScriptRunner{TContext}"/> that adds breakpoint
/// checking and step execution. Does not modify <see cref="ScriptState{TContext}.Execution"/>.
/// </summary>
public sealed class DebugScriptRunner<TContext>(
    ScriptRunner<TContext> runner,
    ScriptDebugToken token,
    BreakpointIndex breakpointIndex,
    ScriptDebugHost host,
    int threadId)
    : IScriptRunner<TContext>
    where TContext : IScriptContext
{
    // Tracks the file+line where we last paused so we don't re-break on the
    // same position immediately after resuming (handles both Continue and Step).
    private string? _lastPausedFile;
    private int _lastPausedLine = -1;

    public ScriptExecution Run(ScriptState<TContext> state)
    {
        while (state.Execution == ScriptExecution.Running)
        {
            state.Next();

            var frame = state.CurrentFrameView;
            var (line, file) = GetLineAndFile(frame);

            // Clear last-paused tracking once we move to a different line.
            if (file != _lastPausedFile || line != _lastPausedLine)
            {
                _lastPausedFile = null;
                _lastPausedLine = -1;
            }

            var shouldPause = token.CheckAndPause(state.FrameDepth, line, out var reason);
            if (!shouldPause && line >= 0 && file != null
                && !(_lastPausedFile == file && _lastPausedLine == line)
                && breakpointIndex.IsBreakpoint(file, line))
            {
                shouldPause = true;
                reason = PauseReason.Breakpoint;
            }

            if (shouldPause)
            {
                _lastPausedFile = file;
                _lastPausedLine = line;
                host.ScriptPaused?.Invoke(threadId, reason);
                token.WaitForResume();
                if (token.IsDisconnected) break;
            }

            var handler = runner.GetHandler(state.OpCode)
                ?? throw new NotImplementedException($"Operation not implemented for OpCode: {state.OpCode}");
            handler.Handle(state);
        }
        return state.Execution;
    }

    private (int line, string? file) GetLineAndFile(FrameView frame)
    {
        if (host.MethodMap == null || !host.MethodMap.TryGetValue(frame.Method, out var entry))
            return (-1, null);

        var lineNumbers = entry.meta.LineNumbers;
        if ((uint)frame.Ip >= (uint)lineNumbers.Length)
            return (-1, null);

        return (lineNumbers[frame.Ip] + 1, entry.meta.FilePath);
    }
}
