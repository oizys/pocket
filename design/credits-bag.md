# Credits Bag: Interactive Game Credits as Inventory

## Concept

Game credits are presented as a bag the player can open and explore. Each letter of each name/role is a unique item occupying one cell in a grid. Portals connect sections so the player can navigate between credit "screens" — e.g. a portal from "Programming" leads to a bag with that team's names spelled out letter by letter.

The player has full agency over the credits: grab letters, rearrange them, sort them into nonsense, steal an "A" from the lead designer's name. The credits are a real bag with real items and real tools — nothing is read-only.

Key features:
- Each letter is a **unique** item (not stackable) — every "A" is distinct, tied to its position in a name
- Role headings (e.g. "DESIGN", "ART") could be category-filtered rows or distinct sub-bags
- Portals link sections together, creating a navigable credits sequence
- The player can exit at any time via normal bag navigation (Q to leave)
- Letters could have descriptions ("The second 'a' in Aaron — load-bearing vowel")
- Rearranging or removing letters has no gameplay consequence — it's pure play

## Cohesion: Very High

This idea uses every core system: bags, grids, unique items, portals, cursor navigation, grab/drop tools, and nested bag hierarchy. It's a showcase of the game's mechanics applied to a traditionally non-interactive element. No new systems required — just creative use of existing ones.

## Intuition: High

Credits as an interactive space is immediately understandable. Players who've been manipulating inventory the whole game will instantly recognize the letter-items as grabbable. The delight is in the realization: "wait, I can mess with this." It rewards curiosity and reinforces that everything in Pockets is just bags all the way down.

## Architecture

### Letter Items

Each letter is a unique `ItemType`:
```
ItemType
├── Name: "Letter A" (or more specific: "A (Aaron, pos 1)")
├── Category: Category.Letter (new category, or reuse a decorative one)
├── IsStackable: false (unique)
└── Properties: { "char": "A", "source": "Aaron", "position": 1 }
```

### Credits Bag Structure

```
Credits Bag (root)
├── Row 0-1: "POCKETS" spelled out (7 letters + spacers)
├── Row 2: portal to "Design" bag, portal to "Code" bag, portal to "Art" bag...
│
├── Design Bag
│   ├── Row 0: "DESIGN" header letters
│   ├── Row 1-N: Names spelled out, one letter per cell
│   └── Portal back to Credits root
│
├── Code Bag
│   ├── Row 0: "CODE" header letters
│   ├── Row 1-N: Names
│   └── Portal back to Credits root
│
└── ... more sections
```

### Generation

A builder function takes structured credit data and generates the bag hierarchy:

```
CreditsBuilder.Build(creditEntries) -> Bag
```

Input: list of `(role, name)` pairs. Output: fully wired bag tree with portals.

### Grid Sizing

Names determine grid width. "AARON" needs 5 columns minimum. Padding cells can be empty or filled with decorative items. Grid height scales with number of names per section.

## Methodology Fit

- **Testable**: Builder function is pure — input credit data, output bag. Assert letter placement, portal connectivity, grid dimensions.
- **Incremental**: Start with a single flat bag of letters, add sections and portals later.
- **Data-driven**: Credit entries could live in a markdown file in `/data`, fitting the existing data loading pattern.
- **Stage-independent**: Can be implemented whenever portals and unique items are stable.

## Status

Proposed — depends on portals being implemented first for cross-section navigation. Could start with a simpler flat-bag version (no portals, just one big grid of letters) as a milestone.
