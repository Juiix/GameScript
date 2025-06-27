using System.Collections.Generic;

namespace GameScript.Language
{
	public static class Constants
	{
		public static readonly IReadOnlyList<string> Keywords = [
			"func",
			"label",
			"return",
			"if",
			"else",
			"while",
			"command",
			"break",
			"returns",
			"continue"
		];

		public static readonly IReadOnlyList<string> Bools = [
			"true",
			"false"
		];

		public static readonly IReadOnlyList<string> AllKeywords = [
			..Keywords,
			..Bools
		];
	}
}
