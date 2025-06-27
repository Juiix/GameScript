using System;
using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	// The base AST node with source file info and an abstract Accept method.
	public abstract class AstNode(string filePath, in FileRange fileRange)
	{
		public string FilePath { get; } = filePath;
		public FileRange FileRange { get; } = fileRange;
		public virtual IEnumerable<AstNode> Children => [];

		// Accept a visitor.
		public abstract void Accept(IAstVisitor visitor);
		public IEnumerable<AstNode> Traverse()
		{
			yield return this;

			foreach (var child in Children)
			{
				foreach (var grand in child.Traverse())
				{
					yield return grand;
				}
			}
		}
	}
}
