using System.Collections.Generic;

namespace GameScript.Language.Bytecode
{
	internal sealed class LoopContext
	{
		public int ConditionIp;
		public int ExitPlaceholder;
		/// <summary>Where 'continue' jumps: the condition for 'while', the increment for 'for'.</summary>
		public int ContinueTargetIp;
		public List<int> BreakPlaceholders = [];
		public List<int> ContinuePlaceholders = [];
	}
}
