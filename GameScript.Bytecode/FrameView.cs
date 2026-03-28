namespace GameScript.Bytecode;

public readonly struct FrameView(BytecodeMethod method, int ip, int stackStart)
{
    public readonly BytecodeMethod Method = method;
    public readonly int Ip = ip;
    public readonly int StackStart = stackStart;
}
