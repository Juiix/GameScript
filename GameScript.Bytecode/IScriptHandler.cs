namespace GameScript.Bytecode;

public interface IScriptHandler<TContext> where TContext : IScriptContext
{
	void Handle(ScriptState<TContext> state);
}
