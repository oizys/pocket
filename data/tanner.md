# Item: Tanner
Category: Structure
Stackable: No

A leather working station for crafting bags and pouches.

# Item: Belt Pouch
Category: Bag
Stackable: No

A small handmade pouch worn on the belt.

# GridTemplate: belt-pouch-3x2
Columns: 3
Rows: 2
Environment: Pouch
ColorScheme: Brown

# Facility: Tanner
Environment: Tanner
ColorScheme: Brown
Recipes: tanner-pouch

# Recipe: tanner-pouch
Name: Belt Pouch
Duration: 5
Grid: 3x1
Input 1: Tanned Leather ×3
Input 2: Woven Fiber ×2
Output: 1 Belt Pouch -> !attach-bag(@belt-pouch-3x2)
