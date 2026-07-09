using System;
using System.Globalization;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class LiteralNode(
		LiteralType type,
		string value,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public LiteralType Type { get; } = type;
		public string Value { get; } = value;

		/// <summary>
		/// Parses a number literal, supporting decimal (123) and hex (0xff) forms.
		/// </summary>
		public static bool TryParseNumber(string value, out int result)
		{
			if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				return int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
			return int.TryParse(value, out result);
		}

		public static int ParseNumber(string value)
		{
			if (!TryParseNumber(value, out var result))
				throw new FormatException($"Invalid number literal '{value}'");
			return result;
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
