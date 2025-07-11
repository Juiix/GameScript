namespace GameScript.Bytecode;

public interface IScriptContext
{
	Value GetValue(int id);
	void SetValue(int id, in Value value);
}
