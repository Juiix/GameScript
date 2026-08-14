using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class IdentifierNode(
		string name,
		IdentifierType type,
		int dotPrefix,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public string Name { get; } = name;
		public IdentifierType Type { get; private set; } = type;
		public int DotPrefix { get; } = dotPrefix;

		/// <summary>
		/// Resolves a bare identifier's kind (parsed as Unknown) to its declared kind.
		/// Written exactly once by NameResolutionVisitor before the AST is published
		/// to analysis consumers; never called on sigil-marked identifiers.
		/// </summary>
		public void ResolveType(IdentifierType type)
		{
			Type = type;
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
