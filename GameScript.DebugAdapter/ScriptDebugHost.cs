using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using GameScript.Bytecode;

namespace GameScript.DebugAdapter;

/// <summary>
/// Thread-safe registry of all actively running script instances.
/// Create one instance per game and keep it alive for the game's lifetime.
/// </summary>
public sealed class ScriptDebugHost
{
    private readonly ConcurrentDictionary<int, ScriptDebugEntry> _entries = new();
    private int _nextThreadId = 1;

    /// <summary>
    /// The compiled program and its debug metadata. Set this once after compilation
    /// (before starting the debug server) so breakpoint validation and stack traces work.
    /// </summary>
    public BytecodeProgram? Program { get; private set; }
    public BytecodeProgramMetadata? Metadata { get; private set; }
    public Dictionary<BytecodeMethod, (int index, BytecodeMethodMetadata meta)>? MethodMap { get; private set; }

    public void SetProgramInfo(BytecodeProgram program, BytecodeProgramMetadata metadata)
    {
        Program = program;
        Metadata = metadata;
        MethodMap = BuildMethodMap(program, metadata);
    }

    /// <summary>
    /// Swap in a new program at runtime (hot reload). Fires <see cref="ProgramReloaded"/>
    /// so the active debug session can re-verify breakpoints and rebuild its method map.
    /// </summary>
    public void ReloadProgram(BytecodeProgram program, BytecodeProgramMetadata metadata)
    {
        Program = program;
        Metadata = metadata;
        MethodMap = BuildMethodMap(program, metadata);
        ProgramReloaded?.Invoke(program, metadata);
    }

    private static Dictionary<BytecodeMethod, (int, BytecodeMethodMetadata)> BuildMethodMap(
        BytecodeProgram program, BytecodeProgramMetadata metadata)
    {
        var map = new Dictionary<BytecodeMethod, (int, BytecodeMethodMetadata)>(program.Methods.Length);
        for (int i = 0; i < program.Methods.Length; i++)
            map[program.Methods[i]] = (i, metadata.MethodMetadata[i]);
        return map;
    }

    /// <summary>
    /// Invoked on the game thread when any script pauses.
    /// Set by <see cref="GameScriptSession"/> on attach; cleared on disconnect.
    /// </summary>
    public Action<int, PauseReason>? ScriptPaused { get; set; }

    /// <summary>
    /// Invoked after <see cref="ReloadProgram"/> swaps in a new program.
    /// Set by <see cref="GameScriptSession"/>; cleared on disconnect.
    /// </summary>
    public Action<BytecodeProgram, BytecodeProgramMetadata>? ProgramReloaded { get; set; }

    /// <summary>
    /// Fired when a script thread is registered or unregistered.
    /// (threadId, isStarted) — used to send DAP 'thread' events.
    /// </summary>
    public event Action<int, bool>? ThreadChanged;

    /// <summary>
    /// Register a script that is about to start running. Returns the assigned thread ID.
    /// Pass the returned ID and token to <see cref="DebugScriptRunner{TContext}"/>.
    /// </summary>
    public int Register(IScriptState state, ScriptDebugToken token, string name)
    {
        var threadId = System.Threading.Interlocked.Increment(ref _nextThreadId);
        _entries[threadId] = new ScriptDebugEntry(threadId, name, state, token);
        ThreadChanged?.Invoke(threadId, true);
        return threadId;
    }

    /// <summary>
    /// Unregister a script after it has finished or been aborted.
    /// </summary>
    public void Unregister(int threadId)
    {
        _entries.TryRemove(threadId, out _);
        ThreadChanged?.Invoke(threadId, false);
    }

    public bool TryGetEntry(int threadId, out ScriptDebugEntry entry) =>
        _entries.TryGetValue(threadId, out entry!);

    public IReadOnlyCollection<ScriptDebugEntry> GetEntries() =>
        (IReadOnlyCollection<ScriptDebugEntry>)_entries.Values;

    /// <summary>
    /// Resume and disconnect all paused scripts. Called by the DAP session on disconnect.
    /// </summary>
    public void DisconnectAll()
    {
        ScriptPaused = null;
        ProgramReloaded = null;
        foreach (var entry in _entries.Values)
            entry.Token.Disconnect();
    }
}
