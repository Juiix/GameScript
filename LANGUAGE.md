# GameScript — Language Reference

> **Scope** This guide covers writing GameScript 2.1: files, types, methods, operators, control flow, and common patterns.
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

**Implicit line joining**: inside `(...)` newlines and indentation are not
significant, so long signatures, call sites, and conditions may wrap freely
(continuation-line indentation is unrestricted):

```gamescript
func input_choice_npc(string text, string c1, string c2, string c3 = "",
        int anim = ^anim_human_still) returns int
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

### Reserved words

`func` `command` `trigger` `return` `returns` `if` `else` `while` `switch`
`case` `default` `for` `in` `break` `continue` `and` `or` `not` `true` `false`
— these cannot be used as identifiers.

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
| `trigger` | `trigger` | —           | ❌        | Declares a game-event dispatch point (see below)     |
| handler   | *(kind)*  | —           | ❌        | Game-event entry point; cannot be called from script |

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

### Default parameter values

Trailing parameters of funcs and commands may declare a default — a literal
(including a negated number) or a `^constant`. Call sites may omit arguments
only from the end; the compiler bakes the default into the call site:

```gamescript
func input_choice_npc(string text, string c1, string c2, string c3 = "",
        int anim = ^anim_human_still) returns int

input_choice_npc("What'll it be?", "Gossip", "Just passing through")
```

Rules:

- Defaults are allowed only on a **contiguous trailing group** of parameters.
- Default values must be compile-time constants — they are baked into the call
  site, so the runtime never sees them.
- If omitting defaults makes a call site match more than one overload, that is
  an ambiguity error — pass the arguments explicitly.
- Trigger handler parameters cannot declare defaults (the host supplies them).

### Triggers

Trigger *kinds* are declared with the `trigger` keyword — one declaration per
engine dispatch point, conventionally collected in `core.gs` next to the
command declarations. The declaration lists the parameters the engine passes,
and its `//` comment is the kind's authoritative doc:

```gamescript
// Player clicks option 1 on a world object; Obj pointer is set
trigger obj_op_1
// NPC script-queue slot 1; Npc pointer is set; args from npc_queue(1, delay, …)
trigger npc_queue_1(int arg0, int arg1)
// Text submitted from a menu input component
trigger mn_text(string text)
```

Trigger *handlers* are entry points fired by the host. They cannot be called
from script and cannot return values. The header format is
`<trigger-kind> <subject>`, stored internally as `"<trigger-kind> <subject>"`.
A handler takes `(...)` only when it declares parameters — an empty `()` is an
error:

```gamescript
// Object interaction
obj_op_1 old_door
    open_door()

// Button on the "hud" menu, "logout" component — menu:component syntax
mn_button_1 hud:logout
    logout()

// A handler with parameters keeps its parens
mn_text username:input(string text)
    validate(text)
```

The compiler validates handler headers against the declarations:

- The trigger kind must be declared — a typo'd kind (`obj_po_1`) is a compile
  error instead of a silently dead handler.
- The handler's parameters must be a **prefix** of the declared parameters —
  handlers may ignore trailing arguments.
- Subjects (`old_door`, `hud:logout`) are **not** validated — they remain
  content-bound names resolved at dispatch time.
- Trigger names share the global namespace with funcs and commands but are
  never callable, so a trigger may not share a name with either.

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

The `..` range operator appears only in `for` headers (`for i in 0..10`) — it
is not a general expression operator.

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

An `if` or `else` body may be written inline as a single statement after a `:`
— the same rule as `case` bodies:

```gamescript
if coin_count() < cost: return false
else: remove_coins(cost)
```

An inline statement and an indented block cannot be combined.

### `switch`

`switch` compares one expression against constant cases. The first matching
case runs and the switch exits — there is no fallthrough. It compiles to the
same bytecode as the equivalent `else if` ladder (with the subject evaluated
once):

```gamescript
func skill_name(int skill) returns string
    switch skill
        case ^skill_attack: return "Attack"        // inline form
        case ^skill_defense: return "Defense"
        case ^skill_mining, ^skill_fishing:        // multiple values per case
            message("gathering skill")             // block form
            return "Gathering"
        default: return "Unknown"
```

- Subjects may be `int`, `string`, or `bool`.
- Case values must be constants: `^const` or literals. Duplicate case values
  are a compile error.
- A case body is either **inline** (a single statement after the `:`) or a
  **block** (indented statements on the following lines) — not both.
- `default` is optional and must be last; with no match and no default the
  switch is skipped.
- A `switch` with a `default` where every arm returns counts as a guaranteed
  return for return-path analysis.
- `break` inside a case body binds to the enclosing **loop** (a switch is not
  a loop).

### Loops

`for` iterates ints over the half-open range `[START, END)`. Both bounds are
evaluated once, before the first iteration. The header declares the loop
variable (always `int`):

```gamescript
for i in 0..inv_size(^inv_backpack)      // half-open: 0, 1, …, size-1
    int itemType = inv_get_item(^inv_backpack, i)
    if itemType == 0
        continue
    if itemType == target
        break
```

Function-flat scoping is unchanged — the loop variable stays visible after the
loop. As a special case, a later `for` in the same func may reuse the same
variable name.

`while` loops on a bare bool condition:

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

`break` and `continue` work in both `for` and `while` (in a `for`, `continue`
still increments). Outside a loop they are compile errors.

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
