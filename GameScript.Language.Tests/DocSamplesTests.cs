using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameScript.Language.Ast;
using GameScript.Language.Tests.Harness;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// Keeps the public documentation honest: every ```gamescript fence in the root
/// Markdown files must parse, and the samples/hello project must analyze clean.
/// A fence that is deliberately a fragment (a bare expression, statements outside
/// a func) is skipped when the line before it is the HTML comment
/// <c>&lt;!-- fragment --&gt;</c>.
/// </summary>
public class DocSamplesTests
{
	private const string FragmentMarker = "<!-- fragment -->";

	private static readonly string RepoRoot = FindRepoRoot();

	public static IEnumerable<object[]> MarkdownFiles() =>
		Directory.GetFiles(RepoRoot, "*.md").OrderBy(f => f).Select(f => new object[] { Path.GetFileName(f) });

	[Theory]
	[MemberData(nameof(MarkdownFiles))]
	public void Gamescript_Fences_Parse(string fileName)
	{
		var fences = ExtractFences(System.IO.File.ReadAllLines(Path.Combine(RepoRoot, fileName)));
		Assert.All(fences, fence =>
		{
			var parser = new AstParser($"{fileName}:{fence.Line}", fence.Code);
			parser.ParseProgram();
			Assert.True(parser.Errors.Count == 0,
				$"{fileName} line {fence.Line}:\n{string.Join("\n", parser.Errors.Select(TestCompilation.FormatError))}\n---\n{fence.Code}");
		});
	}

	[Fact]
	public void Hello_Sample_Analyzes_Without_Diagnostics()
	{
		var dir = Path.Combine(RepoRoot, "samples", "hello");
		var compilation = new TestCompilation();
		foreach (var path in Directory.GetFiles(dir, "*.gs").OrderBy(p => p))
			compilation.AddFile(Path.GetFileName(path), System.IO.File.ReadAllText(path));
		compilation.Analyze();
		Assert.True(!compilation.AllErrors.Any(),
			"Unexpected diagnostics:\n" + string.Join("\n", compilation.AllErrors.Select(TestCompilation.FormatError)));
	}

	private static List<(int Line, string Code)> ExtractFences(string[] lines)
	{
		var fences = new List<(int, string)>();
		for (int i = 0; i < lines.Length; i++)
		{
			var trimmed = lines[i].TrimStart();
			if (trimmed != "```gamescript")
				continue;

			bool fragment = i > 0 && lines[i - 1].Trim() == FragmentMarker;
			var indent = lines[i][..(lines[i].Length - trimmed.Length)];
			int start = i + 1;
			var body = new List<string>();
			for (i = start; i < lines.Length && lines[i].TrimStart() != "```"; i++)
				body.Add(lines[i].StartsWith(indent, StringComparison.Ordinal) ? lines[i][indent.Length..] : lines[i].TrimStart());

			if (!fragment)
				fences.Add((start, string.Join("\n", body) + "\n"));
		}
		return fences;
	}

	private static string FindRepoRoot()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
			if (System.IO.File.Exists(Path.Combine(dir.FullName, "GameScript.sln")))
				return dir.FullName;
		throw new InvalidOperationException("GameScript.sln not found above " + AppContext.BaseDirectory);
	}
}
