namespace GameScript.LanguageServer.Tools
{
	/// <summary>
	/// Provides a quick check for whether a file path refers to a
	/// GameScript-related source file (e.g., <c>.gs</c> or <c>.const</c>).
	/// </summary>
	internal static class ExtensionFilter
	{
		private static readonly HashSet<string> _ext =
			new(StringComparer.OrdinalIgnoreCase) { ".gs", ".const" };

		/// <summary>
		/// Determines whether the specified file should be processed by the language server.
		/// </summary>
		/// <param name="filePath">The full path of the file to test.</param>
		/// <returns>
		/// <see langword="true"/> if the file has a recognized GameScript extension; otherwise, <see langword="false"/>.
		/// </returns>
		public static bool IsGameScript(string filePath) =>
			_ext.Contains(Path.GetExtension(filePath));
	}
}
