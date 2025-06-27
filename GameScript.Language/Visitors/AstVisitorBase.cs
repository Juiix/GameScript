using System.Collections.Generic;
using System.Linq;
using GameScript.Language.Ast;
using GameScript.Language.File;

namespace GameScript.Language.Visitors
{
	public abstract class AstVisitorBase : IAstVisitor
	{
		private readonly List<FileError> _errors = [];

		public IReadOnlyList<FileError> Errors => _errors;

		public virtual void Clear()
		{
			_errors.Clear();
		}

		/// <summary>
		/// Default visit method for a generic AST node.
		/// It simply iterates through the node's children and calls Accept on each one.
		/// </summary>
		public virtual void Visit(AstNode node)
		{
			foreach (AstNode child in node.Children)
			{
				child?.Accept(this);
			}
		}

		// Definitions

		public virtual void Visit(ProgramNode node)
		{
			// Visit all child definition nodes.
			Visit((AstNode)node);
		}

		public virtual void Visit(MethodDefinitionNode node)
		{
			// Default: visit children (keyword, return types, name, parameters, body)
			Visit((AstNode)node);
		}

		public virtual void Visit(ConstantsNode node)
		{
			Visit((AstNode)node);
		}

		// Statements

		public virtual void Visit(IfStatementNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(ElseIfStatementNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(WhileStatementNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(VariableDefinitionNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(ConstantDefinitionNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(ParameterNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(ReturnTypeNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(BlockNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(ReturnStatementNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(ContinueStatementNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(BreakStatementNode node)
		{
			Visit((AstNode)node);
		}

		// Expressions

		public virtual void Visit(LiteralNode node)
		{
			// Leaf node, nothing to do.
		}

		public virtual void Visit(IdentifierNode node)
		{
			// Leaf node, nothing to do.
		}

		public virtual void Visit(TupleExpressionNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(BinaryExpressionNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(UnaryExpressionNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(PostfixExpressionNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(AssignmentExpressionNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(OperatorNode node)
		{
			// Leaf node, nothing to do.
		}

		public virtual void Visit(CallExpressionNode node)
		{
			Visit((AstNode)node);
		}

		public virtual void Visit(UnparsableExpressionNode node)
		{
			// Leaf or dummy node, nothing to do.
		}

		// Other nodes

		public virtual void Visit(KeywordNode node)
		{
			// Leaf node.
		}

		public virtual void Visit(TypeNode node)
		{
			// Leaf node.
		}

		public virtual void Visit(IdentifierDeclarationNode node)
		{
			// Leaf node.
		}

		public virtual void Visit(CommentNode node)
		{
			// Leaf node.
		}

		protected void Error(string message, AstNode node)
		{
			_errors.Add(new FileError(message, node.FileRange));
		}

		protected void Error(string message, IEnumerable<AstNode> nodes)
		{
			_errors.Add(new FileError(message, FileRange.Combine(nodes.Select(x => x.FileRange))));
		}

		protected void Error(string message, in FileRange fileRange)
		{
			_errors.Add(new FileError(message, fileRange));
		}
	}
}
