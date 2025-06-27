using GameScript.Language.Ast;
using GameScript.Language.Index;
using System.Collections.Generic;

namespace GameScript.Language.Visitors
{
	public abstract class AnalysisVisitorBase(
		IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> localIndexes) : AstVisitorBase
	{
		private readonly IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> _localIndexes = localIndexes;

		protected LocalIndex? LocalIndex { get; private set; } = null;
		protected MethodDefinitionNode? Method { get; private set; } = null;

		public override void Visit(MethodDefinitionNode node)
		{
			Method = node;
			LocalIndex = _localIndexes.TryGetValue(node, out var localIndex) ? localIndex : null;
			base.Visit(node);
			LocalIndex = null;
			Method = null;
		}
	}
}
