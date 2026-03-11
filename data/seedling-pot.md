# Item: Seedling Pot
Category: Structure
Stackable: No

A clay pot for growing wilderness bags from seeds.

# Item: Forest Bag
Category: Bag
Stackable: No

A bag containing a small forest wilderness.

# GridTemplate: forest-6x4
Columns: 6
Rows: 4
Environment: Forest
ColorScheme: Green

# LootTableTemplate: forest-materials
Items: Plain Rock ×1.0, Rough Wood ×2.0, Forest Seed ×0.5, Rich Soil ×0.5, Smooth Pebble ×0.5, Iron Ore ×0.3
FillRatio: 0.6

# Facility: Seedling Pot
Environment: Seedling Pot
ColorScheme: Green
Recipes: seedling-forest

# Recipe: seedling-forest
Name: Forest Bag
Duration: 8
Grid: 3x1
Input 1: Forest Seed ×5
Input 2: Rich Soil ×3
Output: 1 Forest Bag -> !wilderness(@forest-6x4, @forest-materials)
