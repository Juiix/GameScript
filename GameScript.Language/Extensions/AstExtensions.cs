namespace GameScript.Language.Ast
{
	public static class AstExtensions
	{
		public static AstNode? FindNodeAtPosition(this AstNode astNode, int position) => astNode.FindNodeAtPosition<AstNode>(position);
		public static AstNode? FindNodeAtPosition(this AstNode astNode, int line, int character) => astNode.FindNodeAtPosition<AstNode>(line, character);

		public static T? FindNodeAtPosition<T>(this AstNode astNode, int position) where T : class
		{
			// If offset is not within this node's range, return null immediately
			if (!astNode.FileRange.Contains(position))
			{
				return default;
			}

			// If offset is within this node's range, check children first
			// to see if there's a more specific node that encloses the offset.
			foreach (var child in astNode.Children)
			{
				var found = child.FindNodeAtPosition<T>(position);
				if (found != null)
				{
					return found;
				}
			}

			// If none of the children match, then 'this' node is the smallest match.
			return astNode as T;
		}

		public static T? FindNodeAtPosition<T>(this AstNode astNode, int line, int character) where T : class
		{
			// If offset is not within this node's range, return null immediately
			if (!astNode.FileRange.Contains(line, character))
			{
				return default;
			}

			// If offset is within this node's range, check children first
			// to see if there's a more specific node that encloses the offset.
			foreach (var child in astNode.Children)
			{
				var found = child.FindNodeAtPosition<T>(line, character);
				if (found != null)
				{
					return found;
				}
			}

			// If none of the children match, then 'this' node is the smallest match.
			return astNode as T;
		}
	}
}
