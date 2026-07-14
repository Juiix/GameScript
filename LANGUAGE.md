# GameScript — Language Reference

> **Scope** This guide covers writing GameScript: files, types, methods, operators, control flow, and common patterns.
>
> Embedding the compiler/VM in a C# game? See **[EMBEDDING.md](EMBEDDING.md)**.

---

## Contents

1. [Files & the Global Namespace](#1-files--the-global-namespace)
2. [Lexical Basics](#2-lexical-basics)
3. [Types & Coercion](#3-types--coercion)
4. [Declarations](#4-declarations)
5. [Method Kinds](#5-method-kinds)
6. [Expressions & Operators](#6-expressions--operators)
7. [Control Flow](#7-control-flow)
8. [Commands & the Host](#8-commands--the-host)
9. [Cookbook](#9-cookbook)

---

## 1 Files & the Global Namespace

| Extension  | Allowed content                | Purpose                                  |
| ---------- | ------------------------------ | ---------------------------------------- |
| `.gs`      | **Only** method declarations   | Funcs, labels, commands, triggers        |
| `.const`   | **Only** constant declarations | Compile-time values (`^name`)            |
| `.context` | **Only** context declarations  | Host-backed variable slots (`%name`)     |

Mixing categories in the same file is a parser error.

**There are no imports.** All files in a project share one global namespace: any `func`, `label`, `^constant`, or `%context` variable is visible from every script. Projects typically organize by feature folder and generate `.const` files as symbol tables for game data (item IDs, menu IDs, sounds, …).

The editor tooling also recognizes **object definition** data files (`.varp`, `.varn`, `.item`, `.npc`, `.menu`, `.obj`, `.tile`, `.inv`, `.anim`, `.param`, `.tex`, `.rig`) — these hold game data referenced from scripts via `^constants`, not GameScript code.

### Indentation

Blocks are indentation-based (no braces). One tab or 4 spaces per level:

```gamescript
label entry()
    if %logged_in
        println("Welcome back!")
```

---

## 2 Lexical Basics

### Comments

```gamescript
// line comment

/* block comment
spanning multiple lines */
```

A comment immediately above a declaration is captured as the symbol's summary and shown in hover tooltips:

```gamescript
// Kills the player and resets their position
command kill_player()
```

### Sigils

Every symbol carries a prefix that makes its role visible at a glance:

| Prefix | Kind        | Declared in   | Example                      |
| ------ | ----------- | ------------- | ---------------------------- |
| `$`    | Local var   | `.gs` body    | `int $counter = 0`           |
| `^`    | Constant    | `.const`      | `int ^max_level = 99`        |
| `%`    | Context var | `.context`    | `int %tutorial_progress = 1` |
| `~`    | Func call   | call site     | `~get_input_str("…")`        |
| `@`    | Label call  | call site     | `@entry()`                   |

Commands and triggers use bare identifiers. Identifiers may contain letters, digits, and underscores.

### Literals

```gamescript
int    $a = 42
int    $b = -7          // negative literals allowed, including in .const files
int    $c = 0x1f        // hex
bool   $d = true
string $e = "Hello"
```

---

## 3 Types & Coercion

GameScript has three scalar value types and a special `label` type for method references.

| Type     | Description                                               |
| -------- | --------------------------------------------------------- |
| `bool`   | Boolean — `true` / `false`                                |
| `int`    | 32-bit signed integer                                     |
| `string` | Immutable text string                                     |
| `label`  | Reference to a `label` method (for passing/scheduling)    |

The type checker is strict in script, but the **runtime boundary is forgiving**: values crossing between script and host coerce between `bool` and `int` (`true` = `1`, non-zero = `true`). In particular, a host can back a `bool %context` variable with int storage.

---

## 4 Declarations

### Constants (`.const`)

Compile-time literals — `int` (decimal, hex, or negative), `bool`, or `string`:

```gamescript
int ^tutorial_killed_rat = 10
int ^temperature_min = -40
int ^color_mask = 0xff00ff
string ^example_message = "Hello, world"
```

### Context variables (`.context`)

Context variables map to per-player (or per-entity) slots provided by the host. The initializer is the **slot ID**, not a default value:

```gamescript
int %tutorial_progress = 1
// A skill value determining the player's damage output
int %skill_strength = 4
```

### Local variables (`.gs` bodies)

```gamescript
int $x = 0
bool $active = true
string $msg = "Hello, " + $name

// multiple declarations of one type
int $a, $b
string $first, $last
```

---

## 5 Method Kinds

| Kind      | Keyword   | Call Syntax | Returns? | Notes                                              |
| --------- | --------- | ----------- | -------- | -------------------------------------------------- |
| `func`    | `func`    | `~name()`   | ✅        | Regular function; returns to caller               |
| `label`   | `label`   | `@name()`   | ❌        | One-way jump (GOTO); execution never returns      |
| `command` | `command` | `name()`    | ✅        | Host-implemented opcode; no body in script        |
| `trigger` | *(type)*  | —           | ❌        | Game-event entry point; cannot be called from script |

### Funcs vs Labels

Use `func` when the caller needs execution to resume (returning a value, querying the host).
Use `label` for one-way control flow — jumping to the next state, starting a new interaction chain, or as a continuation after a suspend.

```gamescript
func multiply_and_add(int $x, int $y) returns int
    return ($x * $y) + ^const_value

label entry()
    int $result = ~multiply_and_add(10, 15)
    println_int($result)
    @next_step()         // one-way jump — never returns here

label next_step()
    println("Done.")
```

Because `@label` calls never push a return frame, a label that jumps to itself loops **without stack growth** — see [§7](#7-control-flow).

### Tuple returns

Declare multiple return values with helper names for documentation:

```gamescript
func get_numbers() returns (int $num1, int $num2)
    return (10, 15)
```

Receive them with a tuple assignment — variables must be declared before the assignment:

```gamescript
int $a, $b
($a, $b) = ~get_numbers()
```

Commands can also declare tuple returns, which is common for host calls that report success plus a payload:

```gamescript
command send_login() returns (bool $success, string $error)
```

### Triggers

Triggers are entry points fired by the host (UI events, NPC interactions, etc.). They cannot be called from script and cannot return values.

The name format is `<trigger-type> <trigger-name>()`, stored internally as `"<trigger-type> <trigger-name>"`. The set of trigger types is defined by your game — common conventions:

```gamescript
// Right-click option 3 on an NPC — numbered interaction slots
npc_op_3 village_elder()
    @talk_elder_main()

// Object interaction
obj_op_1 old_door()
    open_door()

// Button on the "hud" menu, "logout" component — menu:component syntax
mn_button_1 hud:logout()
    logout()

// Lifecycle events
login player_login()
    ~show_welcome()
```

---

## 6 Expressions & Operators

Operators from highest to lowest precedence:

| Level          | Operators                        | Notes                                    |
| -------------- | -------------------------------- | ---------------------------------------- |
| Postfix        | `$x++` `$x--`                    | Increment/decrement a variable           |
| Unary          | `!` `-` `++$x` `--$x`            | Logical not, negation                    |
| Multiplicative | `*` `/`                          |                                          |
| Additive       | `+` `-`                          | `+` also concatenates strings            |
| Relational     | `<` `>` `<=` `>=`                |                                          |
| Equality       | `==` `!=`                        |                                          |
| Logical and    | `and`                            |                                          |
| Logical or     | `or`                             |                                          |
| Assignment     | `=` `+=` `-=` `*=` `/=`          | Also tuple assignment `($a, $b) = …`     |

```gamescript
if $level >= 10 and !%tutorial_done
    $bonus += $level * 2
    $title = "Level " + int_2_str($level)
```

Assignment targets are locals (`$`) and context variables (`%`). Constants are read-only.

---

## 7 Control Flow

### Branching

```gamescript
if %logged_in
    println("Welcome back!")
else if %guest_mode
    println("Browsing as guest.")
else
    @login_flow()
```

There is no `switch` — chain `else if`, or split states across labels.

### Loops

`while` is the only loop construct (no `for`):

```gamescript
int $i = 0
while $i < 10
    if ~should_skip($i)
        $i++
        continue
    if ~is_done($i)
        break
    println_int($i)
    $i++
```

`break` and `continue` work as usual.

### Early returns

Guard clauses are the idiomatic way to validate before acting:

```gamescript
func try_pay(int $cost) returns bool
    if ~coin_count() < $cost
        message("You can't afford that.")
        return false
    remove_coins($cost)
    return true
```

### Label loops

Labels chain control flow without pushing a return frame — useful for multi-step sequences, state machines, and tail-recursive loops:

```gamescript
label loop_test(int $i)
    if $i >= 3
        println("Loop finished")
        @final()
    println_int($i)
    @loop_test($i + 1)     // tail-recursive — no stack growth

label final()
    println("Script complete.")
```

---

## 8 Commands & the Host

Commands declare host-implemented opcodes. No body is allowed in script — the host registers a C# handler for each one. A project's command declarations (conventionally collected in a `core.gs`) are effectively its standard library:

```gamescript
// Print a string value
command print(string $value)
// Convert an integer to its string representation
command int_2_str(int $value) returns string
// Suspend until the player submits a number; resume with the result
command suspend_for_int() returns int
// Enqueue a label to run after a delay (in game ticks)
command queue(label $method, int $delay)
```

### Suspending commands

A command may **pause the script** and resume it later with a result — this is how dialogue and input work. From the script's point of view it's just a call that takes a while:

```gamescript
int $answer = suspend_for_int()   // script sleeps here until the player responds
```

### Scheduling with `label` values

The `label` type passes a method reference to the host for later execution:

```gamescript
command queue(label $method, int $delay)

label explode()
    play_sound(^sound_boom)

label light_fuse()
    queue(@explode, 5)     // run @explode in 5 ticks
```

### Dot-prefixed commands

A command call may be prefixed with dots: `anim(1)`, `.anim(1)`, `..anim(1)`. The dot count is passed to the host along with the call, and its meaning is defined by your game — conventionally `no dot` = act on the current entity and `.` = act on the interaction target:

```gamescript
npc_op_1 guard()
    anim(^anim_wave)      // the player waves
    .anim(^anim_wave)     // the guard waves back
```

Context variables accept a single dot the same way: `%hp` is yours, `.%hp` is the target's. Dots are not allowed on locals or constants.

---

## 9 Cookbook

Common patterns from real projects. Command names are examples — your game defines its own.

### Dialogue tree via label chains

Each label is one node of the conversation; branching is a choice followed by `if`:

```gamescript
npc_op_1 blacksmith()
    @talk_blacksmith_main()

label talk_blacksmith_main()
    ~npc_say("Need something forged?")
    int $choice = ~player_choice_2("Show me your wares.", "Just passing through.")
    if $choice == 1
        @talk_blacksmith_shop()
    ~player_say("Just passing through.")

label talk_blacksmith_shop()
    ~npc_say("Finest steel in the region.")
    open_shop(^shop_blacksmith)
```

### Suspend-for-input helper

Wrap the open-UI / suspend / close-UI dance in a `func` so call sites stay one line:

```gamescript
func input_int(string $prompt) returns int
    mn_open_dialogue(^menu_input_int)
    mn_set_text(^menu_input_int_title, $prompt)
    int $input = suspend_for_int()
    mn_close_dialogue(^menu_input_int)
    return $input
```

### Tick-gated action loop

A context variable stores the next tick an action is allowed, so repeated triggers become a timed loop:

```gamescript
obj_op_1 tree()
    if game_tick() < %action_delay
        return
    if inv_free(^inv_backpack) < 1
        message("Your backpack is full!")
        return
    inv_add(^inv_backpack, ^item_logs, 1)
    add_xp(^skill_woodcutting, 25)
    %action_delay = game_tick() + 4    // one chop per 4 ticks
```

### Menu trigger with a tuple-returning host call

```gamescript
mn_button_1 login:submit()
    ~show_loading("Signing in...")
    bool $success
    string $error
    ($success, $error) = send_login()
    ~hide_loading()
    if !$success
        mn_set_text(^menu_login_error, $error)
        return
    mn_open_dialogue(^menu_character_select)
```

### Deferred continuation with `queue`

Split time-delayed effects into a trigger that schedules and a label that fires:

```gamescript
obj_op_1 gate()
    open_gate()
    queue(@close_gate, 10)     // auto-close after 10 ticks

label close_gate()
    close_gate_now()
    play_sound(^sound_gate_close)
```

---

## License / Contribution

Feel free to open PRs to improve this guide or the engine itself.
