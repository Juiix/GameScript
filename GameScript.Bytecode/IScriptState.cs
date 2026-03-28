using System;

namespace GameScript.Bytecode;

public interface IScriptState
{
    ScriptExecution Execution { get; }
    BytecodeProgram? Program { get; }
    int FrameDepth { get; }
    int CopyFrames(Span<FrameView> destination);
    Value GetLocalInFrame(int frameIndex, int localIndex);
}
