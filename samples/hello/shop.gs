// shop.gs — the Rusty Anvil. TUTORIAL.md builds this file up one step at a time.

string ^shop_name = "Rusty Anvil"

// The player's gold. The initializer is the host's storage slot, not a value.
int @gold = 1
// The item the player holds (0 = nothing)
item @held = 2

// The host starts this func by name when the game loads
func main()
    println("Welcome to the {^shop_name}. You have {@gold} gold.")
    count_down(3)

func count_down(int n)
    for i in 0..n
        print("{n - i}... ")
    println("Open!")

// A trigger handler: the host fires it as "on_talk blacksmith".
// Handlers cannot be called from script and take no '()' when parameterless.
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

func haggle()
    println("Blacksmith: Make me an offer for the sword.")
    int offer = ask_number()
    int price = stock[^item_sword].price       // constant key: folded at compile time
    if offer >= price: return buy(^item_sword)  // 'return f()' in a void func: tail transfer
    println("Blacksmith: {offer}? Get out.")

func close_shop()
    println("The {^shop_name} closes for the night.")

// The host fires this as "on_tick world" and passes the tick number
on_tick world(int tick)
    if tick == 1
        println("The sun sets.")
        queue(close_shop, 2)           // a bare func name is a reference, not a call
