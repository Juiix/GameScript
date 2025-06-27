using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace GameScript.Language.File
{
	public static class FileLoader
	{
		/// <summary>
		/// Reads <paramref name="filePath"/> into a rented <see cref="char"/> array so the caller
		/// can work with <see cref="ReadOnlySpan{T}"/> slices without per-line allocations.
		/// </summary>
		/// <param name="filePath">
		/// Fully-qualified path of the file to load. The file must exist and be smaller than
		/// <see cref="int.MaxValue"/> bytes.
		/// </param>
		/// <param name="chars">
		/// On success, a buffer rented from <see cref="ArrayPool{T}.Shared"/> that contains the file
		/// contents. On failure this is set to <c>null</c>. **The caller must return the buffer**
		/// via <c>ArrayPool&lt;char&gt;.Shared.Return(chars)</c> when finished.
		/// </param>
		/// <param name="length">
		/// Number of valid characters copied into <paramref name="chars"/>.
		/// </param>
		/// <returns>
		/// <see langword="true"/> if the file was read successfully (even if it is empty);
		/// <see langword="false"/> if the file does not exist, is too large, or an I/O error occurs.
		/// Any exceptions are swallowed and surfaced only through the return value.
		/// </returns>
#if NET6_0_OR_GREATER
		public static bool LoadTemporaryFile(string filePath, [MaybeNullWhen(false)] out char[] chars, out int length)
#else
		public static bool LoadTemporaryFile(string filePath, out char[] chars, out int length)
#endif
		{
			chars = null;
			length = 0;

			try
			{
				if (!System.IO.File.Exists(filePath))
				{
					return false;
				}

				long byteLength = new FileInfo(filePath).Length;
				if (byteLength == 0)
					return false;                 // empty file -> empty span

				if (byteLength > int.MaxValue)
				{
					throw new InvalidOperationException($"File too large to parse (>2 GB): {filePath}");
				}

				// Rent a buffer exactly the file size (char count may be smaller for UTF-8,
				// but StreamReader does the right thing and stops when it hits EOF).
				chars = ArrayPool<char>.Shared.Rent((int)byteLength);

				using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
				length = sr.Read(chars, 0, chars.Length);

				return true;
			}
			catch
			{
				if (chars != null)                             // return on failure
				{
					ArrayPool<char>.Shared.Return(chars);
					chars = null;
				}
				throw;
			}
		}
	}
}
