using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GameScript.Language.Index
{
	public abstract class ConcurrentFileSymbolTable<T>
	{
		private readonly Dictionary<string, Dictionary<string, List<T>>> _fileValues = [];
		private readonly Dictionary<string, ConcurrentDictionary<List<T>, int>> _symbolValues = [];
		private readonly ReaderWriterLockSlim _lock = new();

		protected IEnumerable<T> Values => GetValues();

		public void AddFile(string filePath, Dictionary<string, List<T>> symbolValues)
		{
			_lock.EnterWriteLock();
			try
			{
				RemoveFileInner(filePath);

				_fileValues[filePath] = symbolValues;
				foreach (var pair in symbolValues)
				{
					if (!_symbolValues.TryGetValue(pair.Key, out var valueList))
					{
						valueList = [];
						_symbolValues.Add(pair.Key, valueList);
					}

					valueList[pair.Value] = 0;
				}
			}
			finally
			{
				_lock.ExitWriteLock();
			}
		}

		public void RemoveFile(string filePath)
		{
			_lock.EnterWriteLock();
			try
			{
				RemoveFileInner(filePath);
			}
			finally
			{
				_lock.ExitWriteLock();
			}
		}

		protected IEnumerable<T> GetValues()
		{
			_lock.EnterReadLock();
			try
			{
				foreach (var symbols in _symbolValues.Values)
				{
					foreach (var entry in symbols.Keys)
					{
						foreach (var value in entry)
						{
							yield return value;
						}
					}
				}
			}
			finally
			{
				_lock.ExitReadLock();
			}
		}

		protected IEnumerable<T> GetValues(string symbol)
		{
			_lock.EnterReadLock();
			try
			{
				if (!_symbolValues.TryGetValue(symbol, out var symbols))
				{
					yield break;
				}

				foreach (var list in symbols)
				{
					foreach (var reference in list.Key)
					{
						yield return reference;
					}
				}
			}
			finally
			{
				_lock.ExitReadLock();
			}
		}

		protected IEnumerable<T> GetValuesForFile(string filePath)
		{
			_lock.EnterReadLock();
			try
			{
				if (!_fileValues.TryGetValue(filePath, out var symbolValues))
				{
					return [];
				}

				return symbolValues.Values.SelectMany(x => x);
			}
			finally
			{
				_lock.ExitReadLock();
			}
		}

		private void RemoveFileInner(string filePath)
		{
#if NET8_0_OR_GREATER
			if (!_fileValues.Remove(filePath, out var symbolValues))
			{
				return;
			}
#else
			if (!_fileValues.TryGetValue(filePath, out var symbolValues))
			{
				return;
			}
			_fileValues.Remove(filePath);
#endif

			foreach (var pair in symbolValues)
			{
				if (!_symbolValues.TryGetValue(pair.Key, out var valueList))
				{
					continue;
				}

				valueList.TryRemove(pair.Value, out _);
			}
		}
	}
}
