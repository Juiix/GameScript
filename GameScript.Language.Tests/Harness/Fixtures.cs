namespace GameScript.Language.Tests.Harness;

public static class Fixtures
{
	/// <summary>
	/// Command declarations matching the TestOp enum, in the style of a game's core.gs.
	/// </summary>
	public const string CoreGs = """
		// Prints a value to the test log
		command print(string $text)
		// Suspends the script until resumed
		command wait()
		// Converts an int to its string form
		command int_to_str(int $value) returns string
		// Enqueues a method on the strong queue
		command queue_strong(label $method, int $delay)
		// Enqueues a method on the strong queue with one int argument
		command queue_strong_int(label $method, int $delay, int $arg0)
		""";
}
