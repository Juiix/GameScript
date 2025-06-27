using GameScript.LanguageServer.Tools;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace GameScript.LanguageServer.Caches
{
	/// <summary>
	/// In-memory cache for file contents used by the language server.
	/// <para>
	/// • Keeps a dictionary of <c>file-path → full text</c><br/>
	/// • Employs an LRU tracker whose <c>capacity = 64</c> to evict
	///   least-recently-used entries once the warm cache is exceeded.<br/>
	/// • Thread-safe; all public members may be called concurrently.
	/// </para>
	/// </summary>
	internal sealed class TextCache
	{
		private readonly ConcurrentDictionary<string, string> _docs = new();
		private readonly LruCache<string> _lru = new(capacity: 64);

		/// <summary>
		/// Clears any cached text for <paramref name="filePath"/> without touching the LRU list.
		/// </summary>
		public void Clear(string filePath) => _docs.TryRemove(filePath, out _);

		/// <summary>
		/// Removes <paramref name="filePath"/> from both the dictionary and the LRU tracker.
		/// </summary>
		public void Remove(string filePath) => _docs.TryRemove(filePath, out _);

		/// <summary>
		/// Tries to fetch the text for the specified file.
		/// <list type="bullet">
		///   <item><description>If present in the cache, returns it directly.</description></item>
		///   <item><description>If the file exists on disk, reads it, caches it, and returns <see langword="true"/>.</description></item>
		///   <item><description>Otherwise sets <paramref name="text"/> to <see cref="string.Empty"/> and returns <see langword="false"/>.</description></item>
		/// </list>
		/// </summary>
		public bool TryGetText(string filePath, [MaybeNullWhen(false)] out string text)
		{
			if (_docs.TryGetValue(filePath, out text))
			{
				_lru.Touch(filePath);
				return true;
			}

			if (!File.Exists(filePath))
			{
				text = string.Empty;
				return false;
			}

			// Cold → warm promotion
			text = File.ReadAllText(filePath);
			_docs[filePath] = text;
			_lru.Touch(filePath);
			MaybeEvict();
			return true;
		}

		/// <summary>
		/// Inserts or replaces the cached text for <paramref name="filePath"/> and marks it as recently used.
		/// </summary>
		public void Update(string filePath, string text)
		{
			_docs[filePath] = text;
			_lru.Touch(filePath);
			MaybeEvict();
		}

		/// <summary>
		/// Evicts least-recently-used entries until the cache size is within capacity.
		/// </summary>
		private void MaybeEvict()
		{
			foreach (var filePath in _lru.PopOverflow())
				_docs.TryRemove(filePath, out _);
		}
	}
}
