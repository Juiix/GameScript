using GameScript.LanguageServer.Extensions;
using GameScript.LanguageServer.Parsing;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace GameScript.LanguageServer.Caches
{
	/// <summary>
	/// Thread-safe cache that maps an absolute file path to its <see cref="RootFileData"/>
	/// (parse, index, and analysis results).  
	/// Used by request handlers to quickly look up symbol information without
	/// reparsing or re-analysing the file.
	/// </summary>
	internal sealed class AstCache
	{
		private readonly ConcurrentDictionary<string, RootFileData> _rootIndexes = [];

		/// <summary>
		/// Enumerates every cached key. Snapshot-style; the collection is not frozen.
		/// </summary>
		public IEnumerable<string> FilePaths => _rootIndexes.Keys;

		/// <summary>
		/// Enumerates every cached entry. Snapshot-style; the collection is not frozen.
		/// </summary>
		public IEnumerable<RootFileData> Datas => _rootIndexes.Values;

		/// <summary>
		/// Removes the cached AST for the specified file, if present.
		/// </summary>
		public void RemoveFile(string filePath) =>
			_rootIndexes.TryRemove(filePath, out _);

		/// <summary>
		/// Attempts to retrieve the cached <see cref="RootFileData"/> for
		/// <paramref name="filePath"/>.
		/// </summary>
		/// <returns><see langword="true"/> if found; otherwise <see langword="false"/>.</returns>
		public bool TryGetRoot(string filePath, [MaybeNullWhen(false)] out RootFileData rootData) =>
			_rootIndexes.TryGetValue(filePath, out rootData);

		/// <summary>
		/// Trys to removes and set the cached AST for the specified file
		/// </summary>
		public bool TryRemoveFile(string filePath, [MaybeNullWhen(false)] out RootFileData rootData) =>
			_rootIndexes.TryRemove(filePath, out rootData);

		/// <summary>
		/// Inserts or replaces the cache entry for <paramref name="filePath"/>.
		/// </summary>
		public void Update(string filePath, RootFileData rootData) =>
			_rootIndexes[filePath] = rootData;
	}
}
