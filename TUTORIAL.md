# Learn GameScript

> **Who this is for.** You have never written GameScript. You have the VS Code or Visual Studio extension installed (see [README](README.md#get-started)). Each step below adds a few lines to one small project — a blacksmith's shop — and the finished files live in [samples/hello](samples/hello/).
>
> Looking something up instead? The full reference is **[LANGUAGE.md](LANGUAGE.md)**.

---

## Before you start: how GameScript thinks

Four ideas explain most of what follows.

1. **The host defines the standard library.** GameScript has no built-in `print`. Your game (the *host*, a C# program) declares the operations scripts may use as `command`s and implements them in C#. Every project therefore starts with a `core.gs` listing those commands.
2. **There are no imports.** Every `.gs` file in a project folder shares one global namespace. A func declared in `items.gs` is visible from `shop.gs` with no ceremony.
3. **The host decides what runs.** Scripts don't start themselves. The host starts a func by name when it wants to, and fires *trigger handlers* when game events happen. There is no `main` rule — `main` below is just a name the sample host chose.
4. **A `.gs` file can't run on its own.** There is no interpreter to invoke from the command line; scripts run inside a host. The editor gives you errors and hover docs while you write, and [samples/HelloHost](samples/HelloHost/) is a tiny host you can run to see output.

---

## 1. A project folder

Create a folder with two files:

```
rusty-anvil/
  gamescript.json     ← marks the folder as one project (contents are ignored)
  core.gs             ← the commands and triggers the host provides
```

`gamescript.json` can be empty braces: `{}`. The editor treats everything under that folder as one namespace.

`core.gs` declares what the host can do. A `command` has no body — the host implements it — and the comment directly above a declaration becomes its documentation, shown when you hover the name:

```gamescript
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
```

The two `trigger` lines declare *events*. Nothing handles them yet; that comes in step 6.

---

## 2. Your first func

Create `shop.gs`:

```gamescript
func main()
    println("Welcome to the Rusty Anvil.")
```

Two rules you will meet immediately:

- **Blocks are indentation.** Exactly four spaces per level, no tabs, no braces.
- **Statements end at the line end.** No semicolons.

Text in braces inside a string is *interpolated* — any expression works:

```gamescript
func main()
    int visitors = 3
    println("Welcome! You are visitor number {visitors + 1}.")
```

---

## 3. Constants

A constant is a named literal, marked with `^`. Declare it at the top level (outside any func), in any file:

```gamescript
string ^shop_name = "Rusty Anvil"

func main()
    println("Welcome to the {^shop_name}.")
```

Constants are folded in at compile time — they cost nothing at runtime — and the `^` means you can always tell a constant from a variable when reading code.

---

## 4. Context variables

Games need state that outlives a script: the player's gold, quest progress, what they are holding. GameScript calls these *context variables*, marked with `@`. The host owns the storage; the script only names the slot:

```gamescript
// The player's gold. The initializer is the host's storage slot, not a value.
int @gold = 1

func main()
    println("Welcome to the {^shop_name}. You have {@gold} gold.")
```

Read that declaration carefully: `= 1` says "slot 1", not "starts at 1". The sample host puts 40 gold in slot 1 before it runs anything. Scripts read and assign `@gold` like any variable; the host sees every change.

---

## 5. Loops

`for` counts over a half-open range, and `print` (no newline) and `println` are different commands:

```gamescript
func main()
    println("Welcome to the {^shop_name}. You have {@gold} gold.")
    count_down(3)

func count_down(int n)
    for i in 0..n
        print("{n - i}... ")
    println("Open!")
```

`0..3` visits 0, 1, 2. There is also `while`, and `break`/`continue` work in both.

---

## 6. Your first handler

A *handler* is a func the host runs when an event fires. Its header is the trigger kind followed by a *subject* — which NPC, which button, which object. The host fires this one as `"on_talk blacksmith"`:

```gamescript
on_talk blacksmith
    println("Blacksmith: Need something forged?")
```

Three things to notice:

- No `func` keyword, and no `()` — a handler only writes parentheses when it declares parameters (step 11).
- A handler cannot be called from script and cannot return a value. It is an entry point.
- The kind (`on_talk`) must be declared in `core.gs`; a typo is a compile error, not a silently dead handler. The subject (`blacksmith`) is never checked — it is a name the host matches at runtime.

---

## 7. Asking the player something

`ask_number` is declared `returns int`, so call it like a function:

```gamescript
on_talk blacksmith
    println("Blacksmith: Need something forged? (1 = sword, 2 = leave)")
    int choice = ask_number()
    if choice == 1: println("Blacksmith: 25 gold.")
    else: println("Blacksmith: Suit yourself.")
```

Behind that call the script *suspends*: the VM stops, the host shows a prompt, and when the player answers the host resumes the script with the number. From the script's point of view `ask_number()` is just a call that takes a while. This is how dialogue, menus, and input work in GameScript.

`if` conditions are bare — `if (choice == 1)` is an error — and a one-statement body may follow a colon on the same line. Longer bodies go on indented lines:

```gamescript
func greet(int choice)
    if choice == 1
        println("Blacksmith: 25 gold.")
        println("Blacksmith: Finest steel in the region.")
    else if choice == 2
        println("Blacksmith: Suit yourself.")
    else
        println("Blacksmith: Speak up.")
```

---

## 8. Data: a named type and a table

Put game data in its own file, `items.gs`, and it is visible everywhere:

```gamescript
// The id of something the blacksmith sells
type item : int

item ^item_sword  = 1
item ^item_shield = 2
item ^item_potion = 3

// Shop stock: id -> display name and price
table stock(item id, string name, int price)
    ^item_sword,  "Sword",  25
    ^item_shield, "Shield", 15
    ^item_potion, "Potion", 5
```

`type item : int` declares a *named type*: an `item` is stored as an int, but the compiler keeps it distinct, so you cannot pass a menu id where an item id is expected. The three constants are typed `item`.

`table` is a constant table — rows of constants with a lookup syntax. It replaces an `if`/`else` ladder of prices. `stock[id].price` looks a row up by its first column; `stock.has(id)` asks whether a row exists. Tables have no runtime cost beyond the compare chain they compile to; lookups with a constant key (`stock[^item_sword].price`) fold to the value at compile time.

---

## 9. Buying things

Now the shop can sell. Add to `shop.gs`:

```gamescript
// The item the player holds (0 = nothing)
item @held = 2

func buy(item id)
    if not stock.has(id)
        println("Blacksmith: I don't sell that.")
        return
    string name = stock[id].name
    int price = stock[id].price
    if @held == id
        println("Blacksmith: You already have a {name}.")
    else if @gold < price
        println("Blacksmith: A {name} is {price} gold. Come back richer.")
    else
        @gold -= price
        @held = id
        println("Blacksmith: One {name}. {@gold} gold left.")
```

- `not`, `and`, `or` are the logical operators (there is no `!`).
- `return` on its own leaves a func that returns nothing.
- `@gold -= price` writes through to the host's slot.

The player answers with a plain number, and `buy` wants an `item`. Converting needs a *cast*, which is the type name used as a function — `item(choice)`. That is the point of named types: mixing up ids is a compile error rather than a bug in the shipped game. `switch` compares one value against constants:

```gamescript
on_talk blacksmith
    println("Blacksmith: Need something forged?")
    int choice = ask_number()
    switch choice
        case 4: println("Blacksmith: No haggling.")
        case 5: println("Blacksmith: Suit yourself.")
        default: buy(item(choice))
```

Cases don't fall through, and `default` is optional.

---

## 10. A dialogue loop with tail calls

Real conversations loop until the player leaves. GameScript has no goto; instead, a call that is the **last thing a func does** *replaces* the current call frame instead of pushing a new one. That means a func can call itself forever without growing the stack — even across the suspensions in `ask_number`:

```gamescript
on_talk blacksmith
    talk_blacksmith()

func talk_blacksmith()
    println("Blacksmith: Need something forged?")
    for r in stock
        println("  {r.id}) {r.name} - {r.price} gold")
    println("  4) Haggle   5) Leave")
    int choice = ask_number()          // suspends until the host answers
    if choice == 5
        println("Blacksmith: Suit yourself.")
        return
    switch choice
        case 4: haggle()
        default: buy(item(choice))     // int -> item needs a cast
    talk_blacksmith()                  // final statement: tail transfer, no stack growth

func haggle()
    println("Blacksmith: Make me an offer for the sword.")
    int offer = ask_number()
    int price = stock[^item_sword].price       // constant key: folded at compile time
    if offer >= price: return buy(^item_sword)  // 'return f()' in a void func: tail transfer
    println("Blacksmith: {offer}? Get out.")
```

New here:

- `for r in stock` walks the table's rows; `r.name` reads a cell.
- In a func that returns nothing, `return f()` means "transfer to `f` and be done" — handy for leaving early from the middle of a branch. `return` by itself just leaves.
- A func can be used before the line that declares it; order within a project never matters.

---

## 11. Scheduling and handlers with parameters

The `func` type lets a script hand a function to the host to run later. A bare func name in that position is a *reference*, not a call:

```gamescript
func close_shop()
    println("The {^shop_name} closes for the night.")

// The host fires this as "on_tick world" and passes the tick number
on_tick world(int tick)
    if tick == 1
        println("The sun sets.")
        queue(close_shop, 2)           // a bare func name is a reference, not a call
```

`on_tick` was declared with a parameter, so its handler may declare one too — and *now* it writes parentheses. A handler may declare fewer parameters than the trigger provides (a prefix), never more.

Your `shop.gs` now matches [samples/hello/shop.gs](samples/hello/shop.gs).

---

## 12. Running it

The host is in charge of *when* things run. The sample host, [samples/HelloHost/Program.cs](samples/HelloHost/Program.cs), does this:

1. Reads every `.gs` under the folder and compiles them together, printing any diagnostics.
2. Puts 40 gold in slot 1.
3. Starts `main` — the greeting and the countdown.
4. Starts the handler `"on_talk blacksmith"`. Each time the script pauses in `ask_number`, the host reads a number (from the command line, then from you) and resumes it.
5. Starts `"on_tick world"` with `1`, then runs whatever `queue` collected.

From the repo root:

```
dotnet run --project samples/HelloHost -- samples/hello 1 5
```

buys the sword, leaves, and ends with `[host] gold is now 15`. Leave the numbers off to answer interactively. To hook GameScript into your own game, read [EMBEDDING.md](EMBEDDING.md); the sample host is its worked example.

---

## 13. When the compiler complains

The editor underlines problems as you type. The ones every newcomer hits:

| You wrote | The compiler says | Fix |
| --- | --- | --- |
| A tab to indent | `Tabs are not allowed; indent with 4 spaces` | Set the editor to insert spaces. |
| `if (choice == 1)` | `Remove the parentheses around the condition` | `if choice == 1` |
| `!done` | `Use 'not' instead of '!'` | `not done` |
| `int price = 1` twice in one func | `'price' is already defined in this context.` | Locals are visible for the whole func, even when declared inside an `if`. Reuse the first one or pick a new name. |
| `on_talk blacksmith()` | `Trigger handlers with no parameters must omit the '()'` | `on_talk blacksmith` |
| `blacksmith()` | `'blacksmith' is not declared.` | Handlers can't be called; move the body into a `func`. |
| `buy(choice)` with an `int` | `Type mismatch, cannot call 'buy(item)' with 'int'` | `buy(item(choice))` |
| `x++` where `x` is an `item` | `'++' yields 'int' and cannot be stored back into 'item'; write x = item(x + 1).` | Named types don't do arithmetic; cast the result back. |
| `return int_to_str(1)` in a func without `returns` | `... 'return int_to_str(...)' is only allowed when 'int_to_str' returns nothing too.` | Drop the `return`, or declare `returns string`. |
| `string s` then `if s == ""` | *(no error, but never true)* | An unset string is *null*, not empty. Initialize it: `string s = ""`. |

---

## Where next

- **[LANGUAGE.md](LANGUAGE.md)** — everything not covered here: tuple returns, overloads and default parameters, dot-prefixed commands, variadic triggers, the exact rules for tables, named types, scoping, and operators.
- **[EMBEDDING.md](EMBEDDING.md)** — hosting GameScript in a C# game.
- **[CHANGELOG.md](CHANGELOG.md)** — what changed in each release, including breaking changes.
