using GameScript.Language.File;

namespace GameScript.LanguageServer.Parsing
{
	internal sealed class AnalysisResult(
		List<FileError> errors)
	{
		public List<FileError> Errors { get; } = errors;
	}
}
