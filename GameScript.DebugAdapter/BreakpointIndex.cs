using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GameScript.Bytecode;

namespace GameScript.DebugAdapter;

/// <summary>
/// Maps (canonicalized file path, line number) to breakpoint presence.
/// Thread-safe: write on the DAP thread (setBreakpoints), read on the game thread (every instruction).
/// </summary>
public sealed class BreakpointIndex
{
    // outer key: canonical file path; inner key: line number
    private readonly Dictionary<string, HashSet<int>> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    /// Rebuild breakpoints for one source file. Returns the verified lines
    /// (those that actually map to at least one instruction).
    /// </summary>
    public int[] SetBreakpoints(
        string filePath,
        int[] requestedLines,
        BytecodeProgram program,
        BytecodeProgramMetadata metadata)
    {
        var canonical = Canonicalize(filePath);
        var validLines = new HashSet<int>();

        for (int mi = 0; mi < metadata.MethodMetadata.Length; mi++)
        {
            var methodMeta = metadata.MethodMetadata[mi];
            if (!string.Equals(Canonicalize(methodMeta.FilePath), canonical, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var line in methodMeta.LineNumbers)
                validLines.Add(line);
        }

        var verified = new List<int>(requestedLines.Length);
        var active = new HashSet<int>();

        foreach (var line in requestedLines)
        {
            if (validLines.Contains(line))
            {
                active.Add(line);
                verified.Add(line);
            }
        }

        _lock.EnterWriteLock();
        try
        {
            if (active.Count > 0)
                _index[canonical] = active;
            else
                _index.Remove(canonical);
        }
        finally { _lock.ExitWriteLock(); }

        return verified.ToArray();
    }

    /// <summary>
    /// Called by the game thread on every instruction. Must be fast.
    /// </summary>
    public bool IsBreakpoint(string filePath, int line)
    {
        _lock.EnterReadLock();
        try
        {
            return _index.TryGetValue(Canonicalize(filePath), out var lines)
                && lines.Contains(line);
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Remove all breakpoints for all files.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try { _index.Clear(); }
        finally { _lock.ExitWriteLock(); }
    }

    private static string Canonicalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
