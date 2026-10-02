using System.Collections.Concurrent;

namespace GameScript.LanguageServer.Tools;

/// <summary>
/// Gives every file one spelling on case-insensitive file systems (Windows, macOS).
/// <para>
/// The caches and indexes key files by path string, so a file reached as both
/// <c>/Work/Design/core.gs</c> and <c>/Work/design/core.gs</c> (a workspace opened under
/// one casing, a document opened under another) would otherwise be two files that each
/// define the same symbols. The first spelling seen for a path wins, and a new path takes
/// its folder's known spelling, so a workspace keeps the casing the client opened it with.
/// </para>
/// Thread-safe. On case-sensitive file systems every path is its own spelling.
/// </summary>
internal sealed class PathSpellings(bool ignoreCase)
{
	/// <summary>The spellings for this machine's file system, used for all cache/index keys.</summary>
	public static PathSpellings Default { get; } =
		new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

	private readonly bool _ignoreCase = ignoreCase;
	private readonly ConcurrentDictionary<string, string> _spellings = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Returns the spelling to use for <paramref name="fullPath"/> (an absolute path with
	/// relative segments already resolved).
	/// </summary>
	public string Canonicalize(string fullPath)
	{
		if (!_ignoreCase || fullPath.Length == 0)
			return fullPath;

		if (_spellings.TryGetValue(fullPath, out var known))
			return known;

		// a trailing separator is kept as written; the spelling is the folder's
		var trimmed = Path.TrimEndingDirectorySeparator(fullPath);
		if (trimmed.Length != fullPath.Length)
			return Canonicalize(trimmed) + fullPath[trimmed.Length..];

		var parent = Path.GetDirectoryName(fullPath);
		var spelling = string.IsNullOrEmpty(parent)
			? fullPath    // a root
			: Path.Join(Canonicalize(parent), Path.GetFileName(fullPath));

		return _spellings.GetOrAdd(fullPath, spelling);
	}

	/// <summary>
	/// Drops the spelling of a deleted or renamed path and of everything beneath it, so
	/// the next sighting (a rename that only changes case, say) is taken as written.
	/// </summary>
	public void Forget(string fullPath)
	{
		if (!_ignoreCase)
			return;

		fullPath = Path.TrimEndingDirectorySeparator(fullPath);
		_spellings.TryRemove(fullPath, out _);

		var prefix = fullPath + Path.DirectorySeparatorChar;
		foreach (var key in _spellings.Keys)
		{
			if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				_spellings.TryRemove(key, out _);
		}
	}
}
