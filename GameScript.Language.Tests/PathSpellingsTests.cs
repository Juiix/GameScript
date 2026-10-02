using System.IO;
using GameScript.LanguageServer.Tools;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// One spelling per file in the language server: on a case-insensitive file system a
/// file reached under two casings must be one cache/index key, not two files that
/// each define the same symbols.
/// </summary>
public sealed class PathSpellingsTests
{
	private static readonly string Root = Path.Combine(Path.GetTempPath(), "gs-spellings");

	private static string At(params string[] parts) => Path.Combine([Root, .. parts]);

	[Fact]
	public void The_First_Spelling_Of_A_Path_Wins()
	{
		var spellings = new PathSpellings(ignoreCase: true);

		var first = spellings.Canonicalize(At("Design", "scripts", "core.gs"));
		var second = spellings.Canonicalize(At("design", "Scripts", "CORE.gs"));

		Assert.Equal(At("Design", "scripts", "core.gs"), first);
		Assert.Equal(first, second);
	}

	[Fact]
	public void A_New_File_Takes_Its_Folders_Known_Spelling()
	{
		var spellings = new PathSpellings(ignoreCase: true);

		// the workspace root is seen first, as the client opened it
		spellings.Canonicalize(At("Design") + Path.DirectorySeparatorChar);

		var file = spellings.Canonicalize(At("design", "scripts", "new.gs"));

		Assert.Equal(At("Design", "scripts", "new.gs"), file);
	}

	[Fact]
	public void A_Trailing_Separator_Is_Kept()
	{
		var spellings = new PathSpellings(ignoreCase: true);
		spellings.Canonicalize(At("Design", "core.gs"));

		var folder = spellings.Canonicalize(At("design") + Path.DirectorySeparatorChar);

		Assert.Equal(At("Design") + Path.DirectorySeparatorChar, folder);
	}

	[Fact]
	public void Case_Sensitive_File_Systems_Keep_Every_Spelling()
	{
		var spellings = new PathSpellings(ignoreCase: false);

		spellings.Canonicalize(At("Design", "core.gs"));
		var other = spellings.Canonicalize(At("design", "core.gs"));

		Assert.Equal(At("design", "core.gs"), other);
	}

	[Fact]
	public void A_Forgotten_Path_Is_Respelled_On_Its_Next_Sighting()
	{
		var spellings = new PathSpellings(ignoreCase: true);
		spellings.Canonicalize(At("Design", "scripts", "core.gs"));

		// a case-only rename of the file: the folder keeps its spelling
		spellings.Forget(At("Design", "scripts", "core.gs"));
		Assert.Equal(At("Design", "scripts", "Core.gs"), spellings.Canonicalize(At("design", "scripts", "Core.gs")));

		// a removed folder takes everything beneath it along
		spellings.Forget(At("Design", "scripts"));
		Assert.Equal(At("Design", "SCRIPTS", "core.gs"), spellings.Canonicalize(At("design", "SCRIPTS", "core.gs")));
	}
}
