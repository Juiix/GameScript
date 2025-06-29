namespace GameScript.LanguageServer
{
	[Flags]
	internal enum ProgramFlags
	{
		None = 0,
		Debug = 1,
		Vscode = 2,
		VisualStudio = 4
	}
}
