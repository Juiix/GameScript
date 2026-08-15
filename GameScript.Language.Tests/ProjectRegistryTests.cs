using System;
using System.IO;
using GameScript.LanguageServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// Sub-project mapping in the language server: a gamescript.json marker makes a
/// folder an isolated project with its own symbol/reference tables.
/// </summary>
public sealed class ProjectRegistryTests : IDisposable
{
	private readonly string _root;

	public ProjectRegistryTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "gs-projects-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(_root, "server", "scripts"));
		Directory.CreateDirectory(Path.Combine(_root, "client"));
		System.IO.File.WriteAllText(Path.Combine(_root, "server", "gamescript.json"), "{}");
		System.IO.File.WriteAllText(Path.Combine(_root, "client", "gamescript.json"), "{}");
	}

	public void Dispose()
	{
		try { Directory.Delete(_root, true); } catch { }
	}

	private ProjectRegistry CreateRegistry()
	{
		var registry = new ProjectRegistry(NullLogger<ProjectRegistry>.Instance);
		registry.SetRoot(_root + Path.DirectorySeparatorChar);
		return registry;
	}

	[Fact]
	public void Discovers_Marker_Projects_Plus_Default()
	{
		var registry = CreateRegistry();
		// server, client, and the workspace-root default project
		Assert.Equal(3, registry.Projects.Count);
	}

	[Fact]
	public void Files_Map_To_The_Nearest_Project_Root()
	{
		var registry = CreateRegistry();

		var serverFile = Path.Combine(_root, "server", "scripts", "core.gs");
		var clientFile = Path.Combine(_root, "client", "core.gs");
		var rootFile = Path.Combine(_root, "readme.gs");

		var serverProject = registry.GetProject(serverFile);
		var clientProject = registry.GetProject(clientFile);
		var rootProject = registry.GetProject(rootFile);

		Assert.NotSame(serverProject, clientProject);
		Assert.NotSame(serverProject, rootProject);
		Assert.Contains("server", serverProject.RootPath);
		Assert.Contains("client", clientProject.RootPath);
		Assert.Same(registry.Projects[^1], rootProject);    // default project
	}

	[Fact]
	public void Projects_Have_Isolated_Symbol_Tables()
	{
		var registry = CreateRegistry();

		var serverProject = registry.GetProject(Path.Combine(_root, "server", "a.gs"));
		var clientProject = registry.GetProject(Path.Combine(_root, "client", "a.gs"));

		Assert.NotSame(serverProject.Symbols, clientProject.Symbols);
		Assert.NotSame(serverProject.References, clientProject.References);
	}

	[Fact]
	public void Rebuild_Is_A_NoOp_When_Roots_Are_Unchanged()
	{
		var registry = CreateRegistry();
		var before = registry.Projects;

		Assert.False(registry.Rebuild());          // e.g. a marker content edit
		Assert.Same(before, registry.Projects);    // tables preserved
	}

	[Fact]
	public void Rebuild_Detects_New_And_Removed_Projects()
	{
		var registry = CreateRegistry();

		Directory.CreateDirectory(Path.Combine(_root, "tools"));
		System.IO.File.WriteAllText(Path.Combine(_root, "tools", "gamescript.json"), "{}");
		Assert.True(registry.Rebuild());
		Assert.Equal(4, registry.Projects.Count);

		System.IO.File.Delete(Path.Combine(_root, "tools", "gamescript.json"));
		Assert.True(registry.Rebuild());
		Assert.Equal(3, registry.Projects.Count);
	}

	[Fact]
	public void Nested_Project_Shadows_Its_Parent()
	{
		Directory.CreateDirectory(Path.Combine(_root, "server", "plugin"));
		System.IO.File.WriteAllText(Path.Combine(_root, "server", "plugin", "gamescript.json"), "{}");

		var registry = CreateRegistry();

		var pluginFile = Path.Combine(_root, "server", "plugin", "x.gs");
		var serverFile = Path.Combine(_root, "server", "x.gs");
		Assert.NotSame(registry.GetProject(serverFile), registry.GetProject(pluginFile));
		Assert.Contains("plugin", registry.GetProject(pluginFile).RootPath);
	}

	[Fact]
	public void Marker_File_Detection()
	{
		Assert.True(ProjectRegistry.IsMarkerFile(@"c:\content\server\gamescript.json"));
		Assert.True(ProjectRegistry.IsMarkerFile(@"c:\content\server\GameScript.Json"));
		Assert.False(ProjectRegistry.IsMarkerFile(@"c:\content\server\core.gs"));
		Assert.False(ProjectRegistry.IsMarkerFile(@"c:\content\gamescript.json.bak"));
	}
}
