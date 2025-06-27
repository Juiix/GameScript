using GameScript.Language.File;

namespace GameScript.Language.Ast
{
	public abstract class ExpressionNode(
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
	}
}
