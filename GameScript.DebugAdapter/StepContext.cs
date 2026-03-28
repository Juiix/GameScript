namespace GameScript.DebugAdapter;

public enum StepKind { Over, In, Out }

public sealed class StepContext(StepKind kind, int baseFrameDepth, int baseLine = -1)
{
    public StepKind Kind { get; } = kind;
    public int BaseFrameDepth { get; } = baseFrameDepth;
    /// <summary>Source line the step began on. -1 = unknown (pause at next instruction).</summary>
    public int BaseLine { get; } = baseLine;
}
