using OmniSharp.Extensions.JsonRpc.Server;

namespace GameScript.LanguageServer
{
	internal static class ExceptionHelper
	{
		public static void ThrowFileVersionNotFound(object? requestId = null) => throw new ContentModifiedException(requestId);
	}
}
