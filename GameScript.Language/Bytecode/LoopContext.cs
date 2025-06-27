using System.Collections.Generic;

namespace GameScript.Language.Bytecode
{
	internal sealed class LoopContext
	{
		public int ConditionIp;
		public int ExitPlaceholder;
		public List<int> BreakPlaceholders = [];
		public List<int> ContinuePlaceholders = [];
	}
}
