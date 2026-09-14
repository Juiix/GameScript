// items.gs — game data as declarations.
//
// There are no imports: everything here is visible from every file in the project.

// The id of something the blacksmith sells. A named type keeps item ids
// from being mixed up with ordinary ints (or with other kinds of id).
type item : int

item ^item_sword  = 1
item ^item_shield = 2
item ^item_potion = 3

// Shop stock: id -> display name and price
table stock(item id, string name, int price)
    ^item_sword,  "Sword",  25
    ^item_shield, "Shield", 15
    ^item_potion, "Potion", 5
