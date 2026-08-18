using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	// A '.member' access on a table or table row:
	//   t.count            (Arguments == null)
	//   t.has(k) / t.at(i) (Arguments != null — parenthesised call form)
	//   t[k].col / t.at(i).col / r.col
	// Members are identified by name downstream ('count', 'has', 'at' are the
	// built-ins; anything else names a column). Member.Type is IdentifierType.Column.
	public sealed class MemberExpressionNode(
		ExpressionNode target,
		IdentifierNode member,
		IdentifierNode? keyColumn,
		List<ExpressionNode>? arguments,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public const string CountMember = "count";
		public const string HasMember = "has";
		public const string AtMember = "at";

		public ExpressionNode Target { get; } = target;
		public IdentifierNode Member { get; } = member;
		/// <summary>The 'name' of a 't.has(name: k)' call; null otherwise.</summary>
		public IdentifierNode? KeyColumn { get; } = keyColumn;
		/// <summary>Null when written without parentheses ('t.count'); a (possibly empty) list otherwise.</summary>
		public List<ExpressionNode>? Arguments { get; } = arguments;

		public bool IsCount => Member.Name == CountMember;
		public bool IsHas => Member.Name == HasMember;
		public bool IsAt => Member.Name == AtMember;
		public bool IsBuiltin => IsCount || IsHas || IsAt;

		public static bool IsReservedMemberName(string name) =>
			name is CountMember or HasMember or AtMember;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Target;
				yield return Member;
				if (KeyColumn != null)
				{
					yield return KeyColumn;
				}
				if (Arguments != null)
				{
					foreach (var arg in Arguments)
					{
						yield return arg;
					}
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
