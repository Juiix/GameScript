namespace GameScript.Language.Tests.Harness;

public static class Fixtures
{
	/// <summary>
	/// Command and trigger declarations matching the TestOp enum, in the style of a
	/// game's core.gs. queue_strong demonstrates the 2.0 overload style: one script
	/// name mapped onto several engine ops via '=' bindings.
	/// </summary>
	public const string CoreGs = """
		// Prints a value to the test log
		command print(string text)
		// Suspends the script until resumed
		command wait()
		// Converts an int to its string form
		command int_to_str(int value) returns string
		// Enqueues a method on the strong queue
		command queue_strong(func method, int delay)
		// Enqueues a method on the strong queue with one int argument
		command queue_strong(func method, int delay, int arg0) = queue_strong_int

		// Fired when the player logs in
		trigger login
		// Player clicks option 1 on a world object; Obj pointer is set
		trigger obj_op_1
		// A menu button is pressed
		trigger mn_button
		// Text submitted from a menu input component
		trigger mn_text(string text)
		// NPC script-queue slot 1; args from npc_queue(1, delay, ...)
		trigger npc_queue_1(int arg0, int arg1)
		""";
}
