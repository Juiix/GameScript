namespace GameScript.LanguageServer.Tools;

/// <summary>
/// Thread-safe least-recently-used (LRU) key tracker.  
/// <para>
/// The structure stores up to <paramref name="capacity"/> keys; any excess keys
/// can be reclaimed by calling <see cref="PopOverflow"/>, which returns them in
/// LRU order so the caller can dispose of associated resources.
/// </para>
/// </summary>
/// <typeparam name="TKey">The key type used by the cache.</typeparam>
/// <param name="capacity">
/// Maximum number of keys to keep; values <= 0 are coerced to 1.
/// </param>
public sealed class LruCache<TKey>(int capacity)
	where TKey : notnull
{
	private readonly int _capacity = Math.Max(1, capacity);
	private readonly LinkedList<TKey> _list = [];
	private readonly Dictionary<TKey, LinkedListNode<TKey>> _map = [];
	private readonly object _lock = new();

	public void Remove(TKey key)
	{
		lock (_lock)
		{
			if (_map.TryGetValue(key, out var node))
			{
				_list.Remove(node);
				_map.Remove(key);
			}
		}
	}

	/// <summary>
	/// Marks <paramref name="key"/> as the most recently used,
	/// inserting it if it is not already present.
	/// </summary>
	public void Touch(TKey key)
	{
		lock (_lock)
		{
			if (_map.TryGetValue(key, out var node))
			{
				_list.Remove(node);
				_list.AddFirst(node);
			}
			else
			{
				var n = new LinkedListNode<TKey>(key);
				_list.AddFirst(n);
				_map[key] = n;
			}
		}
	}

	/// <summary>
	/// Removes and returns keys that exceed the configured capacity,
	/// ordered from least-recently-used to most-recently-used.
	/// </summary>
	public IEnumerable<TKey> PopOverflow()
	{
		lock (_lock)
		{
			while (_map.Count > _capacity)
			{
				var last = _list.Last!;
				_list.RemoveLast();
				_map.Remove(last.Value);
				yield return last.Value;
			}
		}
	}
}
