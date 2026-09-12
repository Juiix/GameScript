namespace GameScript.LanguageServer.Tools
{
	/// <summary>
	/// Provides a quick check for whether a file path refers to a GameScript source
	/// file (<c>.gs</c> — constants, contexts and types are ordinary top-level
	/// declarations since 2.5, so the legacy <c>.const</c>/<c>.context</c> extensions
	/// are no longer source kinds) or to an object-definition data file.
	/// </summary>
	internal static class ExtensionFilter
	{
		private static readonly HashSet<string> _ext =
			new(StringComparer.OrdinalIgnoreCase) { ".gs" };

		private static readonly HashSet<string> _objectDefExt =
			new(StringComparer.OrdinalIgnoreCase)
			{ ".varp", ".item", ".npc", ".menu", ".obj", ".tile", ".inv", ".anim", ".param", ".tex", ".rig", ".varn", ".fx", ".option" };

		/// <summary>
		/// Determines whether the specified file should be processed by the language server.
		/// </summary>
		/// <param name="filePath">The full path of the file to test.</param>
		/// <returns>
		/// <see langword="true"/> if the file has a recognized GameScript extension; otherwise, <see langword="false"/>.
		/// </returns>
		public static bool IsGameScript(string filePath) =>
			_ext.Contains(Path.GetExtension(filePath));

		/// <summary>
		/// Determines whether the specified file is an object definition data file.
		/// </summary>
		public static bool IsObjectDef(string filePath) =>
			_objectDefExt.Contains(Path.GetExtension(filePath));
	}
}
