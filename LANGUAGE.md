# GameScript — Language Reference

> **Scope** This guide covers writing GameScript 2.0: files, types, methods, operators, control flow, and common patterns.
>
> Embedding the compiler/VM in a C# game? See **[EMBEDDING.md](EMBEDDING.md)**.
>
> Migrating 1.x content? See the **[CHANGELOG](CHANGELOG.md)** for the full list of syntax changes.

---

## Contents

1. [Files & the Global Namespace](#1-files--the-global-namespace)
2. [Lexical Basics](#2-lexical-basics)
3. [Types & Coercion](#3-types--coercion)
4. [Declarations](#4-declarations)
5. [Method Kinds](#5-method-kinds)
6. [Expressions & Operators](#6-expressions--operators)
7. [Control Flow & Tail Calls](#7-control-flow--tail-calls)
8. [Commands & the Host](#8-commands--the-host)
9. [Cookbook](#9-cookbook)

---

## 1 Files & the Global Namespace

| Extension  | Allowed content                | Purpose                                  |
| ---------- | ------------------------------ | ---------------------------------------- |
| `.gs`      | **Only** method declarations   | Funcs, commands, triggers                |
| `.const`   | **Only** constant declarations | Compile-time values (`^name`)            |
| `.context` | **Only** context declarations  | Host-backed variable slots (`@name`)     |

Mixing categories in the same file is a parser error.

**There are no imports.** All files in a project share one global namespace: any `func`, `^constant`, or `@context` variable is visible from every script. Projects typically organize by feature folder and generate `.const` files as symbol tables for game data (item IDs, menu IDs, sounds, …).

The editor tooling also recognizes **object definition** data files (`.varp`, `.varn`, `.item`, `.npc`, `.menu`, `.obj`, `.tile`, `.inv`, `.anim`, `.param`, `.tex`, `.rig`, `.fx`, `.option`) — these hold game data referenced from scripts via `^constants`, not GameScript code.

### Indentation

Blocks are indentation-based (no braces). **Exactly 4 spaces per level; tabs are errors**, and indentation that isn't a multiple of 4 is an error:

```gamescript
func entry()
    if @logged_in
        println("Welcome back!")
```

Statements end at the newline — semicolons are illegal.

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

### Name marks

Only two marks exist; everything else is a bare identifier resolved by declaration:

| Mark   | Kind        | Declared in   | Example                      |
| ------ | ----------- | ------------- | ---------------------------- |
| `^`    | Constant    | `.const`      | `^max_level`                 |
| `@`    | Context var | `.context`    | `@tutorial_progress`         |

Locals, params, funcs, commands, and triggers are bare identifiers: `count`,
`skill_name(skill)`, `queue(close_gate, 10)`. The compiler resolves bare names
against declarations; a local may not share a name with any func or command
(compile error), so bare names are never ambiguous.

Convention: `snake_case` for funcs/commands/triggers/constants/context vars, `camelCase` for locals.

### Literals

```gamescript
int    a = 42
int    b = -7          // negative literals allowed, including in .const files
int    c = 0x1f        // hex
bool   d = true
string e = "Hello"
```

### String interpolation

`{expr}` inside a string literal embeds any expression, with the same coercion
rules as `+` concatenation. `{{` and `}}` produce literal braces:

```gamescript
message("Congratulations, your {skill_name(skill)} level is now {after}!")
```

Interpolation is parser-only sugar — it compiles to the same bytecode as the
equivalent `+` chain.

---

## 3 Types & Coercion

GameScript has three scalar value types and a special `func` type for method references.

| Type     | Description                                               |
| -------- | --------------------------------------------------------- |
| `bool`   | Boolean — `true` / `false`                                |
| `int`    | 32-bit signed integer                                     |
| `string` | Immutable text string                                     |
| `func`   | Reference to a func (for passing/scheduling)              |

The type checker is strict in script, but the **runtime boundary is forgiving**: values crossing between script and host coerce between `bool` and `int` (`true` = `1`, non-zero = `true`). In particular, a host can back a `bool @context` variable with int storage.

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
int @tutorial_progress = 1
// A skill value determining the player's damage output
int @skill_strength = 4
```

### Local variables (`.gs` bodies)

```gamescript
int x = 0
bool active = true
string msg = "Hello, " + name

// multiple declarations of one type
int a, b
string first, last
```

Locals may not shadow a func or command name — pick a different name.

---

## 5 Method Kinds

| Kind      | Keyword   | Call Syntax | Returns? | Notes                                                |
| --------- | --------- | ----------- | -------- | ---------------------------------------------------- |
| `func`    | `func`    | `name()`    | ✅        | Script routine; calls in tail position tail-transfer |
| `command` | `command` | `name()`    | ✅        | Host-implemented opcode; no body in script           |
| `trigger` | *(type)*  | —           | ❌        | Game-event entry point; cannot be called from script |

Funcs and commands share one call syntax. Two declarations may share a name if
their **parameter signatures** differ (overloading, see [§8](#8-commands--the-host));
return types don't participate.

> **Where did `label` go?** 1.x split script routines into `func` and `label`
> (one-way jumps). In 2.0 everything is a `func`: a call that is the last thing
> a func does compiles to a **tail transfer** — the current frame is replaced,
> so state-machine chains and dialogue loops still run with zero stack growth.
> See [§7](#7-control-flow--tail-calls).

```gamescript
func multiply_and_add(int x, int y) returns int
    return (x * y) + ^const_value

func entry()
    int result = multiply_and_add(10, 15)
    println(result)
    next_step()          // final statement → tail transfer, no stack growth

func next_step()
    println("Done.")
```

### Tuple returns

Declare multiple return values with helper names for documentation:

```gamescript
func get_numbers() returns (int num1, int num2)
    return (10, 15)
```

Receive them with a tuple assignment — declaring inline is the idiomatic form:

```gamescript
(int a, int b) = get_numbers()

// or into existing variables, or a mix:
int a
(a, int b) = get_numbers()
```

Commands can also declare tuple returns, which is common for host calls that report success plus a payload:

```gamescript
command send_login() returns (bool success, string error)
```

### Triggers

Triggers are entry points fired by the host (UI events, NPC interactions, etc.). They cannot be called from script and cannot return values.

The name format is `<trigger-type> <trigger-name>`, stored internally as `"<trigger-type> <trigger-name>"`. A trigger header takes `(...)` only when it declares parameters — an empty `()` is an error. The set of trigger types is defined by your game — common conventions:

```gamescript
// Right-click option 3 on an NPC — numbered interaction slots
npc_op_3 village_elder
    talk_elder_main()

// Object interaction
obj_op_1 old_door
    open_door()

// Button on the "hud" menu, "logout" component — menu:component syntax
mn_button_1 hud:logout
    logout()

// A trigger with parameters keeps its parens
mn_text username:input(string text)
    validate(text)
```

---

## 6 Expressions & Operators

Operators from highest to lowest precedence:

| Level          | Operators                        | Notes                                    |
| -------------- | -------------------------------- | ---------------------------------------- |
| Postfix        | `x++` `x--`                      | Increment/decrement a variable           |
| Unary          | `not` `-` `++x` `--x`            | Logical not, negation                    |
| Multiplicative | `*` `/` `%`                      | `%` is modulo                            |
| Additive       | `+` `-`                          | `+` also concatenates strings            |
| Relational     | `<` `>` `<=` `>=`                |                                          |
| Equality       | `==` `!=`                        |                                          |
| Logical and    | `and`                            |                                          |
| Logical or     | `or`                             |                                          |
| Assignment     | `=` `+=` `-=` `*=` `/=` `%=`     | Also tuple assignment `(a, b) = …`       |

The `!` prefix is removed — write `not`. (`!=` is unaffected.)

```gamescript
if level >= 10 and not @tutorial_done
    bonus += level * 2
    title = "Level {level}"
```

Assignment targets are locals and context variables (`@`). Constants are read-only.

---

## 7 Control Flow & Tail Calls

### Branching

Conditions are bare — wrapping the whole condition in parentheses is an error
(`if (x)` → `if x`). Inner grouping is fine: `if (a or b) and c`.

```gamescript
if @logged_in
    println("Welcome back!")
else if @guest_mode
    println("Browsing as guest.")
else
    login_flow()
```

There is no `switch` — chain `else if`.

### Loops

`while` is the only loop construct (no `for`):

```gamescript
int i = 0
while i < 10
    if should_skip(i)
        i++
        continue
    if is_done(i)
        break
    println(i)
    i++
```

### Early returns

```gamescript
func try_pay(int cost) returns bool
    if coin_count() < cost
        message("You can't afford that.")
        return false
    remove_coins(cost)
    return true
```

### Tail calls

A func call in **tail position** compiles to a frame *replacement*, not a stack
push. Tail position means:

- `return f(...)` where `f`'s return arity matches the current func's, or
- a call as the final statement of a func with no return values.

This is what makes long state-machine chains and self-recursive loops safe — the
call stack never grows, even across suspends:

```gamescript
func loop_test(int i)
    if i >= 3
        println("Loop finished")
        return final()          // tail transfer
    println(i)
    loop_test(i + 1)            // final statement → tail transfer, no stack growth

func final()
    println("Script complete.")
```

A call that is *not* in tail position (a result is used, or statements follow)
is an ordinary call and returns normally.

---

## 8 Commands & the Host

Commands declare host-implemented opcodes. No body is allowed in script — the host registers a C# handler for each one. A project's command declarations (conventionally collected in a `core.gs`) are effectively its standard library:

```gamescript
// Print a value
command print(string value)
// Convert an integer to its string representation
command int_2_str(int value) returns string
// Suspend until the player submits a number; resume with the result
command suspend_for_int() returns int
```

### Overloading and `=` op bindings

Funcs and commands may **overload**: same name, different parameter signatures
(count or types). Call sites resolve by argument count and types; a call that
matches no overload — or would be ambiguous — is a compile error. Return types
do not participate in overload resolution.

Each command overload binds to its own engine op. By default the binding is the
declared name; `= internal_name` binds explicitly, which lets one script name
fan out to several engine ops:

```gamescript
// Enqueue a func on the strong queue with a delay
command queue_strong(func method, int delay) = queue_strong
// … with one int argument
command queue_strong(func method, int delay, int arg0) = queue_strong_int
// … with one string argument
command queue_strong(func method, int delay, string arg0) = queue_strong_str
```

The compiler resolves the overload and emits the bound op id — the engine's
handler registry is untouched.

### Suspending commands

A command may **pause the script** and resume it later with a result — this is how dialogue and input work. From the script's point of view it's just a call that takes a while:

```gamescript
int answer = suspend_for_int()   // script sleeps here until the player responds
```

Any func — including one reached through tail transfers — can suspend.

### Scheduling with `func` values

The `func` type passes a method reference to the host for later execution. A
bare func name in a `func`-typed argument position is a reference, not a call:

```gamescript
command queue(func method, int delay)

func explode()
    play_sound(^sound_boom)

func light_fuse()
    queue(explode, 5)      // run explode in 5 ticks
```

Any func is queueable; a queued func's return values are discarded.

### Dot-prefixed commands

A command call may be prefixed with dots: `anim(1)`, `.anim(1)`, `..anim(1)`. The dot count is passed to the host along with the call, and its meaning is defined by your game — conventionally `no dot` = act on the current entity and `.` = act on the interaction target:

```gamescript
npc_op_1 guard
    anim(^anim_wave)      // the player waves
    .anim(^anim_wave)     // the guard waves back
```

Context variables accept a single dot the same way: `@hp` is yours, `.@hp` is the target's. Dots are not allowed on locals or constants.

---

## 9 Cookbook

Common patterns from real projects. Command names are examples — your game defines its own.

### Dialogue tree via tail-call chains

Each func is one node of the conversation; branching is a choice followed by `if`:

```gamescript
npc_op_1 blacksmith
    talk_blacksmith_main()

func talk_blacksmith_main()
    npc_say("Need something forged?")
    int choice = player_choice_2("Show me your wares.", "Just passing through.")
    if choice == 1
        return talk_blacksmith_shop()   // tail transfer
    player_say("Just passing through.")

func talk_blacksmith_shop()
    npc_say("Finest steel in the region.")
    open_shop(^shop_blacksmith)
```

### Suspend-for-input helper

```gamescript
func input_int(string prompt) returns int
    mn_open_dialogue(^menu_input_int)
    mn_set_text(^menu_input_int_title, prompt)
    int input = suspend_for_int()
    mn_close_dialogue(^menu_input_int)
    return input
```

### Tick-gated action loop

A context variable stores the next tick an action is allowed, so repeated triggers become a timed loop:

```gamescript
obj_op_1 tree
    if game_tick() < @action_delay
        return
    if inv_free(^inv_backpack) < 1
        message("Your backpack is full!")
        return
    inv_add(^inv_backpack, ^item_logs, 1)
    add_xp(^skill_woodcutting, 25)
    @action_delay = game_tick() + 4    // one chop per 4 ticks
```

### Menu trigger with a tuple-returning host call

```gamescript
mn_button_1 login:submit
    show_loading("Signing in...")
    (bool success, string error) = send_login()
    hide_loading()
    if not success
        mn_set_text(^menu_login_error, error)
        return
    mn_open_dialogue(^menu_character_select)
```

### Deferred continuation with `queue`

Split time-delayed effects into a trigger that schedules and a func that fires:

```gamescript
obj_op_1 gate
    open_gate()
    queue(close_gate, 10)      // auto-close after 10 ticks

func close_gate()
    close_gate_now()
    play_sound(^sound_gate_close)
```

---

## License / Contribution

Feel free to open PRs to improve this guide or the engine itself.
