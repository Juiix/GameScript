namespace GameScript.Language.File
{
	public enum FileErrorSeverity
	{
		Error,
		Warning,
		Information,
		Hint
	}

	public enum FileErrorTag
	{
		None,
		Unnecessary,
		Deprecated
	}

	public readonly struct FileError
	{
		public string Message { get; }
		public FileRange FileRange { get; }
		public FileErrorSeverity Severity { get; }
		public FileErrorTag Tag { get; }

		public FileError(string message, in FileRange fileRange)
			: this(message, fileRange, FileErrorSeverity.Error, FileErrorTag.None)
		{
		}

		public FileError(string message, in FileRange fileRange, FileErrorSeverity severity, FileErrorTag tag)
		{
			Message = message;
			FileRange = fileRange;
			Severity = severity;
			Tag = tag;
		}
	}
}
