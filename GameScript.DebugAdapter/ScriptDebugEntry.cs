using GameScript.Bytecode;

namespace GameScript.DebugAdapter;

public sealed class ScriptDebugEntry(int threadId, string name, IScriptState state, ScriptDebugToken token)
{
    public int ThreadId { get; } = threadId;
    public string Name { get; } = name;
    public IScriptState State { get; } = state;
    public ScriptDebugToken Token { get; } = token;
}
