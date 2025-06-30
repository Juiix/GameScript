using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GameScript.Language.Bytecode
{
	internal sealed class CommandHandler<T> where T : struct, Enum
	{
		private readonly static Dictionary<string, ushort> s_commandOps =
#if NET6_0_OR_GREATER
			Enum.GetValues<T>()
#else
			Enum.GetValues(typeof(T)).Cast<T>()
#endif
			.Where(x => Convert.ToUInt16(x) >= 1000)
			.ToDictionary(GenerateCommandName, x => Convert.ToUInt16(x));

		public static bool TryGetOp(string command, out ushort op) =>
			s_commandOps.TryGetValue(command, out op);

		private static string GenerateCommandName(T opCode)
		{
			var input = opCode.ToString();

			var builder = new StringBuilder();
			char last = default;
			for (int i = 0; i < input.Length; i++)
			{
				char c = input[i];
				if (char.IsUpper(c) || (char.IsDigit(c) && !char.IsDigit(last)))
				{
					if (i > 0)
						builder.Append('_');

					builder.Append(char.ToLower(c));
				}
				else
				{
					builder.Append(c);
				}
				last = c;
			}
			return builder.ToString();
		}
	}
}
