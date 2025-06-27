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

		[Export(typeof(FileExtensionToContentTypeDefinition))]
		[ContentType("gamescript")]
		[FileExtension(".gs")]
		public static FileExtensionToContentTypeDefinition GameScriptFileExtension { get; set; }

		[Export(typeof(FileExtensionToContentTypeDefinition))]
		[ContentType("gamescript")]
		[FileExtension(".const")]
		public static FileExtensionToContentTypeDefinition ConstantFileExtension { get; set; }
	}
}
