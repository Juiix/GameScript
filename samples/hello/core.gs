// core.gs — the host's standard library.
//
// A command has no body: the host implements it in C# (see samples/HelloHost).
// A trigger names an event the host fires; handlers for it live in other files.
// The comment directly above a declaration is its documentation (shown on hover).

// Writes text with no newline
command print(string text)
// Writes text followed by a newline
command println(string text)
// Pauses the script until the host supplies a number
command ask_number() returns int
// Runs a func after 'delay' ticks
command queue(func method, int delay)

// The player talks to an NPC
trigger on_talk
// A tick of game time has passed
trigger on_tick(int tick)
