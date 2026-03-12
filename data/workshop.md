# Item: Workshop
Category: Structure
Stackable: No

A basic crafting station for building other facilities.

# Facility: Workshop
Environment: Workshop
ColorScheme: Gray
Recipes: workshop-workbench, workshop-tanner, workshop-seedling-pot

# Recipe: workshop-workbench
Name: Workbench
Duration: 5
Grid: 3x1
Input 1: Plain Rock ×8
Input 2: Rough Wood ×4
Output: 1 Workbench -> !attach-facility(@Workbench)

# Recipe: workshop-tanner
Name: Tanner
Duration: 5
Grid: 3x1
Input 1: Tanned Leather ×6
Input 2: Woven Fiber ×3
Output: 1 Tanner -> !attach-facility(@Tanner)

# Recipe: workshop-seedling-pot
Name: Seedling Pot
Duration: 4
Grid: 3x1
Input 1: Rich Soil ×4
Input 2: Rough Wood ×2
Output: 1 Seedling Pot -> !attach-facility(@Seedling Pot)
