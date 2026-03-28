namespace GameScript.Bytecode;

public interface IScriptRunner<TContext> where TContext : IScriptContext
{
    ScriptExecution Run(ScriptState<TContext> state);
}
