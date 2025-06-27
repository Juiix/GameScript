namespace GameScript.Language.File
{
	public readonly struct FileError
	{
		public string Message { get; }
		public FileRange FileRange { get; }

		public FileError(string message, in FileRange fileRange)
		{
			Message = message;
			FileRange = fileRange;
		}
	}
}
