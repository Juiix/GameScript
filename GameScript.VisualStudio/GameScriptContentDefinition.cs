using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace GameScript.VisualStudio
{
	internal static class GameScriptContentDefinition
	{
		[Export(typeof(ContentTypeDefinition))]
		[Name("gamescript")]
		[BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
		public static ContentTypeDefinition GameScriptContentType { get; set; }

		// .gs is the only source extension: constants, contexts and types are ordinary
		// top-level declarations since 2.5 (the legacy .const/.context files are renamed)
		[Export(typeof(FileExtensionToContentTypeDefinition))]
		[ContentType("gamescript")]
		[FileExtension(".gs")]
		public static FileExtensionToContentTypeDefinition GameScriptFileExtension { get; set; }
	}
}
