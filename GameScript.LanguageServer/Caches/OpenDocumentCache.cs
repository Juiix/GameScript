using GameScript.LanguageServer.Tools;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace GameScript.LanguageServer.Caches
{
	/// <summary>
	/// In-memory cache of *open* documents for the language server.
	/// <para>
	/// • Maps <c>file-path → (text, version)</c><br/>
	/// • LRU-evicts once the hot set exceeds <c>Capacity</c> (default 64).<br/>
	/// • Thread-safe – all members may be invoked concurrently.
	/// </para>
	/// </summary>
	internal sealed class OpenDocumentCache
	{
		private const int Capacity = 64;

		private readonly ConcurrentDictionary<string, Entry> _docs =
			new(StringComparer.OrdinalIgnoreCase);

		private readonly LruCache<string> _lru = new(capacity: Capacity);

		/*──────────────────────────── API ───────────────────────────*/

		/// <summary>Removes any cached state for <paramref name="filePath"/> (LRU untouched).</summary>
		public void Clear(string filePath) =>
			_docs.TryRemove(filePath, out _);

		/// <summary>Removes <paramref name="filePath"/> from both cache and LRU tracker.</summary>
		public void Remove(string filePath)
		{
			_docs.TryRemove(filePath, out _);
			_lru.Remove(filePath);
		}

		/// <summary>
		/// Tries to fetch the current text and version.
		/// Returns <see langword="true"/> if the document is in the cache.
		/// </summary>
		public bool TryGet(
			string filePath,
			[MaybeNullWhen(false)] out string text,
			out int? version)
		{
			if (_docs.TryGetValue(filePath, out var entry))
			{
				_lru.Touch(filePath);
				text = entry.Text;
				version = entry.Version;
				return true;
			}

			text = null;
			version = default;
			return false;
		}

		/// <summary>
		/// Inserts or replaces the cached text + version and marks it as recently used.
		/// </summary>
		public void Update(string filePath, string text, int version)
		{
			_docs[filePath] = new(text, version);
			_lru.Touch(filePath);
			MaybeEvict();
		}

		/*────────────────── implementation details ─────────────────*/

		private void MaybeEvict()
		{
			foreach (var victim in _lru.PopOverflow())
				_docs.TryRemove(victim, out _);
		}

		private readonly record struct Entry(string Text, int Version);
	}
}
