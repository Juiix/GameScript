using System.Collections.Generic;
using GameScript.Language.Ast;
using GameScript.Language.File;

namespace GameScript.Language.Visitors
{
	// The IAstVisitor interface.
	public interface IAstVisitor
	{
		IReadOnlyList<FileError> Errors { get; }


		void Clear();

		// File nodes
		void Visit(ProgramNode node);
		void Visit(ConstantsNode node);
		void Visit(ContextsNode node);

		// Definition nodes
		void Visit(MethodDefinitionNode node);
		void Visit(ConstantDefinitionNode node);
		void Visit(ContextDefinitionNode node);

		// Statement nodes
		void Visit(IfStatementNode node);
		void Visit(ElseIfStatementNode node);
		void Visit(WhileStatementNode node);
		void Visit(ParameterNode node);
		void Visit(BlockNode node);
		void Visit(VariableDefinitionNode node);
		void Visit(ReturnStatementNode node);
		void Visit(ContinueStatementNode continueStatementNode);
		void Visit(BreakStatementNode breakStatementNode);

		// Expression nodes
		void Visit(LiteralNode node);
		void Visit(IdentifierNode node);
		void Visit(TupleExpressionNode node);
		void Visit(BinaryExpressionNode node);
		void Visit(UnaryExpressionNode node);
		void Visit(PostfixExpressionNode node);
		void Visit(AssignmentExpressionNode node);
		void Visit(OperatorNode node);
		void Visit(CallExpressionNode node);
		void Visit(UnparsableExpressionNode node);

		// Other nodes
		void Visit(KeywordNode node);
		void Visit(TypeNode node);
		void Visit(ReturnTypeNode node);
		void Visit(IdentifierDeclarationNode node);
		void Visit(CommentNode node);
	}
}
