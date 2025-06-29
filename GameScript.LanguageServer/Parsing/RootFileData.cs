using GameScript.Language.Ast;
using GameScript.Language.File;

namespace GameScript.LanguageServer.Parsing
{
	internal sealed record RootFileData(
		ParseResult Parse,
		IndexResult Index,
		AnalysisResult Analysis)
	{
		public AstNode Root => Parse.Root;
		public int? FileVersion => Parse.FileVersion;

		public IReadOnlyList<FileError> Errors => [
			.. Parse.Errors,
			.. Index.Errors,
			.. Analysis.Errors,
		];
	}
}
