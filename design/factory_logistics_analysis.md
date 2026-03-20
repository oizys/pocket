# Factory Automation Games: Logistics Primitives Analysis

## Games Surveyed (20)

| # | Game | Perspective | Year |
|---|------|------------|------|
| 1 | **Factorio** | Top-down 2D | 2020 |
| 2 | **Satisfactory** | First-person 3D | 2024 |
| 3 | **Dyson Sphere Program** | Third-person 3D (planetary) | 2021 |
| 4 | **Shapez** (1) | Top-down 2D (abstract) | 2020 |
| 5 | **Shapez 2** | Top-down 3D (abstract) | 2024 |
| 6 | **Mindustry** | Top-down 2D (tower defense hybrid) | 2017 |
| 7 | **Techtonica** | First-person 3D (underground) | 2023 |
| 8 | **Foundry** | First-person 3D (voxel) | 2024 |
| 9 | **Captain of Industry** | Third-person 3D (colony sim) | 2022 |
| 10 | **Arknights: Endfield** | Third-person 3D (gacha/RPG hybrid) | 2025 |
| 11 | **Factory Town** | Third-person 3D (village sim) | 2021 |
| 12 | **Timberborn** | Third-person 3D (beaver colony) | 2021 |
| 13 | **Nova Lands** | Top-down 2D (pixel art) | 2023 |
| 14 | **Astroneer** | Third-person 3D (exploration) | 2019 |
| 15 | **Oxygen Not Included** | Side-view 2D (colony sim) | 2019 |
| 16 | **Minifactory** | Top-down 2D (puzzle) | 2021 |
| 17 | **Automachef** | Top-down 2D (food production) | 2019 |
| 18 | **Builderment** | Top-down 2D (mobile) | 2022 |
| 19 | **Shapez Industries** / misc puzzle-factories | Various | Various |
| 20 | **Final Upgrade** | Top-down 2D | 2024 |

---

## Part 1: The Core Logistics Primitive Set

Every factory game must solve a fundamental problem: how do items move from point A to point B, and how do they enter and exit production buildings? The solutions decompose into a remarkably consistent set of primitives:

### 1.1 Conveyors / Belts (Universal — present in ~100% of games)

The conveyor belt is the single most universal element. Every game in this survey has some form of directional item transport along a path. Key variations:

- **Lane count**: Factorio's belts carry items on two parallel lanes per belt tile, allowing two different item types side-by-side. This is unique — nearly every other game uses single-lane belts (Satisfactory, DSP, Foundry, Techtonica, Mindustry, Shapez 2, etc.).
- **Speed tiers**: Almost universal. Most games offer 3 tiers (Factorio has 4 with Space Age: yellow/red/blue/turbo). Satisfactory has 6 marks. DSP has 3. Techtonica has 3.
- **Belt elevation / 3D pathing**: 3D games (Satisfactory, DSP, Foundry, Techtonica) allow belts to rise and fall along slopes. DSP's belts can be raised and lowered to pass over each other on a planetary grid. Factorio (2D) solves crossing with underground belts instead.
- **Curved vs. grid-locked**: Most 2D games have belts that snap to a grid and auto-curve at corners. 3D games like Satisfactory allow freeform belt pathing with spline curves.

### 1.2 Machine-to-Belt Interface (The Big Dividing Line)

This is where the genre splits into two major design families:

#### Pattern A: "Inserter/Arm" Games — Indirect coupling
Items do NOT flow directly between belts and machines. An intermediate entity (an arm, sorter, grabber) must physically pick up an item and place it.

| Game | Name for the arm | Filtering? | Reach variants? |
|------|-----------------|-----------|----------------|
| **Factorio** | Inserter | Filter inserter; stack inserter | Standard (1 tile), Long-handed (2 tiles) |
| **Dyson Sphere Program** | Sorter | Built-in filter on any sorter | MK.I/II/III (speed tiers), 1-3 tile reach |
| **Techtonica** | Inserter | Filter Inserter; Stack Filter | Standard (1 tile), Long (2 tiles) |
| **Foundry** | Loader | Filter Loader | Standard, 2nd-lane, 3rd-lane loaders |
| **Mindustry** (partially) | — | Sorter blocks, Overflow/Underflow gates | N/A (buildings accept from adjacent belts) |

In these games, the inserter/sorter is a major gameplay element — it creates throughput bottlenecks, spatial puzzles about which side of a machine to load from, and a natural progression system (faster arms = faster production). Factorio's inserter is iconic: a rotating arm that swings items from source to destination, one at a time (or in stacks with upgrades).

DSP's "Sorter" combines inserter + filter into one entity. Every building requires sorters to move items in and out. Sorters can bridge belt-to-building, building-to-building, or belt-to-belt, and each can be given a filter. This is more flexible than Factorio's inserters which only go building↔belt or building↔building.

Foundry's "Loader" is notable as a hybrid: it sits at the machine port and acts as a built-in input/output adapter, rather than a freestanding arm. Loaders connect machines to adjacent belts. There are reach variants (1st-lane, 2nd-lane, 3rd-lane) that function similarly to Factorio's standard vs. long-handed inserter.

#### Pattern B: "Direct Port" Games — Belts plug straight into machines
The belt connects directly to a machine's input or output port. No intermediate arm is needed.

| Game | How it works |
|------|-------------|
| **Satisfactory** | Machines have fixed input/output port positions with directional connectors. Belts snap directly to ports. |
| **Arknights: Endfield** | Facilities have input/output ports. Conveyor belts connect port-to-port. |
| **Captain of Industry** | Conveyors connect directly between machine ports. |
| **Shapez** (1 & 2) | Buildings have fixed directional I/O. Belts auto-connect by adjacency. |
| **Builderment** | Buildings have ports; belts connect directly. |

In these games, throughput is governed by belt speed alone (or machine processing speed), not by an intermediary. The design puzzle shifts from "how many arms do I need and where" to "how do I route belts between fixed port positions." Satisfactory's machines, for instance, have a specific number of input and output ports at fixed positions on the building model, and you simply run a belt to each one.

#### Pattern C: Hybrid / Implicit
Some games blend both approaches:

- **Mindustry**: Buildings accept items from any adjacent conveyor automatically (no explicit arm), but routers, sorters, and gates exist as inline belt devices that control flow. Buildings can also output back to adjacent belts, which creates the infamous "router clog" problem where outputs feed back into inputs.
- **Oxygen Not Included**: Uses duplicant (colonist) labor and conveyor rails, with loader/receptacle buildings to interface.

### 1.3 Splitters (Present in ~85% of games)

A device that takes one input belt and divides its contents across multiple outputs.

| Game | Splitter type | Ports | Filtering? |
|------|-------------|-------|-----------|
| **Factorio** | Splitter | 2-in / 2-out (2×1 tile) | Yes (priority + item filter per output) |
| **Satisfactory** | Conveyor Splitter | 1-in / 3-out | Smart Splitter variant adds filter + overflow |
| **Dyson Sphere Program** | Splitter | 4 ports (any can be in/out) | Yes (filter + priority per port) |
| **Shapez 2** | Inline split | 1-in / up to 3-out (created by dragging off an existing belt) | No |
| **Mindustry** | Router / Sorter / Gates | Variable | Sorter filters by item; overflow/underflow gates |
| **Techtonica** | Auto-split | Created automatically when a belt branches off another | No explicit block |
| **Foundry** | Conveyor Balancer | 2-in / 2-out | Priority + filter on higher tiers |
| **Captain of Industry** | Flat Balancer / Flat Sorter | Up to 8 ports (any in/out) | Sorter filters; Balancer has priority |
| **Arknights: Endfield** | Splitter | Unlocked via logistics tech tree | Basic split; also has Control Ports for filtering |

**Notable subpattern — Bidirectional/Omni-port splitters**: DSP's splitter has 4 ports that can each be input or output, with 3 mode configurations (4-way, parallel 2-way, perpendicular 2-way). Captain of Industry's Flat Balancer similarly has 8 ports assignable as input or output. This contrasts sharply with Satisfactory's fixed 1-in/3-out topology and Factorio's fixed 2-in/2-out topology.

### 1.4 Mergers (Present in ~80% of games)

The inverse of a splitter — combines multiple input streams into one output belt.

| Game | Merger type | Notes |
|------|-----------|-------|
| **Satisfactory** | Conveyor Merger | 3-in / 1-out (separate building from splitter) |
| **Factorio** | (implicit) | Belt sideloading + splitters. No dedicated "merger" block — you merge by feeding a belt into the side of another belt. |
| **DSP** | (via splitter) | The 4-port splitter doubles as a merger. |
| **Shapez 2** | Inline merge | Created by dragging multiple belts into one. Up to 3-in / 1-out. |
| **Mindustry** | Junction / Router | Conveyors meeting head-on merge at ~1:1; routers distribute from multiple inputs. |
| **Techtonica** | Auto-merge | Created automatically when belts join together. |
| **Foundry** | (via Balancer) | The 2-in/2-out balancer also serves as a merger. |
| **Captain of Industry** | Flat Balancer / Connector | Connector is simpler; balancer allows priority. |
| **Arknights: Endfield** | Converger | Dedicated merge block, unlocked via tech tree. |

**Key distinction**: Satisfactory and Arknights: Endfield have splitters and mergers as distinct buildings (you cannot use a splitter to merge). Factorio has no dedicated merger at all — merging is done via belt mechanics. DSP and Captain of Industry use omnidirectional port blocks that serve as both.

### 1.5 Underground / Tunnel / Bridge Belts (Present in ~75% of games)

A mechanism to cross belts without interference.

| Game | Mechanism | Max distance |
|------|----------|-------------|
| **Factorio** | Underground Belt (entry + exit pair) | 4/6/8 tiles by tier |
| **Mindustry** | Bridge Conveyor (entry + exit) | 3 tiles; Phase Conveyor: 11 tiles |
| **Mindustry** | Junction (1×1 crossing block) | Instant cross, no distance |
| **DSP** | Belt elevation (raise/lower belts to cross over each other) | N/A |
| **Satisfactory** | Belt elevation + Conveyor Lift | N/A (3D routing solves it) |
| **Shapez 2** | Tunnels (space belts) | Between platforms |
| **Arknights: Endfield** | Bridge | Unlocked via logistics tech |
| **Techtonica** | Vertical belts (stacking) | Vertical stacking solves crossing |
| **Foundry** | Belt slopes / Freight Elevator | 3D routing + elevator |

**Subpattern**: 2D games (Factorio, Mindustry, Shapez 1) *must* have an explicit crossing mechanism because belts share a flat plane. 3D games typically solve crossing by routing belts at different heights, making dedicated underground/bridge systems less necessary (though some, like Arknights: Endfield, still include them for convenience).

Factorio's underground belts are particularly notable because they can "braid" — different tiers of underground belt can overlap on the same tiles, allowing very dense belt routing.

### 1.6 Vertical Transport / Lifts (Present in ~60% of games, only in 3D)

| Game | Mechanism |
|------|----------|
| **Satisfactory** | Conveyor Lift (1-48m vertical, upgradeable in 6 tiers) |
| **Foundry** | Conveyor Slope + Freight Elevator |
| **Techtonica** | Vertical Conveyor Belts (stackable) |
| **DSP** | Belt height adjustment (continuous) |
| **Shapez 2** | Conveyor Lifts (between floors) |
| **Arknights: Endfield** | Not prominent — flat factory layout |

Satisfactory's Conveyor Lift is a dedicated building. Foundry introduced Freight Elevators for long vertical runs. DSP handles verticality as a property of the belt itself (belts can be drawn at any height). Techtonica uses explicit vertical belt segments.

### 1.7 Storage / Buffers (Universal — ~100%)

Every game has some form of item storage. Key variations:

- **Passive chests/containers**: Factorio (wooden/iron/steel chests), Satisfactory (Storage Container, Industrial Storage Container), Techtonica (Container), DSP (Depot Mk.I/II), Foundry (Logistic Container), Captain of Industry (storages).
- **Active/logistics storage**: Factorio has logistic chests (requester, provider, buffer, storage) that interface with the logistics robot network. DSP has Planetary/Interstellar Logistics Stations that function as mega-buffers with drone/vessel distribution. Satisfactory's storage is passive only.
- **Inline buffers**: Some splitters/mergers have small internal buffers (Satisfactory's splitter buffers 9 items; DSP's splitter can have a Depot placed on top of it).
- **Central depot model**: Arknights: Endfield uses a central PAC storage that all production chains feed into and draw from. Mining rigs deposit directly to storage wirelessly. This is unusual — most games require physical belt connections.

### 1.8 Filtering / Sorting (Present in ~70% of games)

The ability to separate mixed item streams:

- **On the inserter/arm**: Factorio (Filter Inserter), Techtonica (Filter Inserter), DSP (any Sorter can be filtered), Foundry (Filter Loader).
- **On the splitter**: Factorio (splitter has item filter + priority), Satisfactory (Smart Splitter, Programmable Splitter), DSP (splitter output filter + priority), Captain of Industry (Flat Sorter).
- **Dedicated sorter block**: Mindustry (Sorter block: passes one item type straight, routes others to the side. Inverted Sorter does the opposite).
- **Control ports**: Arknights: Endfield has Control Ports that filter specific items on belts.

---

## Part 2: Design Pattern Taxonomy

### Pattern 1: "Inserter Sandwich" (Factorio-family)
**Games**: Factorio, Dyson Sphere Program, Techtonica, Foundry

The canonical layout is: **Belt → Inserter → Machine → Inserter → Belt**

Machines have no direct belt connections. Every item transfer requires an explicit arm/sorter/loader entity. This creates:
- A throughput constraint at every machine boundary (inserter speed × count).
- Spatial puzzles about arm placement, reach, and machine orientation.
- A natural tech progression (faster/smarter arms unlock higher throughput).
- The "inserter dance" — optimizing how many inserters feed each machine from which belt lanes.

Factorio is the purest expression: machines have a generic 3×3 or similar footprint with no designated ports. Inserters can be placed on any side, picking from any adjacent entity. DSP is similar but sorters have explicit grid-snapping to building ports. Techtonica requires inserters but machines have visible port indicators. Foundry's loaders are fixed to machine ports, making it more structured.

### Pattern 2: "Direct Plumbing" (Satisfactory-family)
**Games**: Satisfactory, Arknights: Endfield, Captain of Industry, Shapez 1 & 2, Builderment

Machines have **fixed, directional, typed ports**. Each port accepts one belt connection. The belt IS the interface. This creates:
- No throughput bottleneck at the machine boundary (belt speed = machine input rate).
- Spatial puzzles about routing belts to fixed port positions (especially in 3D where port locations on the building model matter).
- Emphasis on the belt network topology rather than per-machine loading optimization.
- Often more "what you see is what you get" — the port count on a machine directly tells you its capacity.

Satisfactory's machines have clearly labeled input (orange ≡) and output (green 〕) ports at specific positions on the 3D model. You simply run a belt to each port. No intermediate step. Shapez takes this even further — buildings have fixed directional I/O on specific tile faces, and belts auto-connect by adjacency.

### Pattern 3: "Omni-Accept" (Mindustry-family)
**Games**: Mindustry, some simpler/mobile factory games

Buildings accept items from **any adjacent conveyor or building**, with no explicit port or arm. Items flow in if the building needs them and has space. This creates:
- Very fast iteration and compact builds.
- The "router problem" — because inputs and outputs aren't strictly separated, items can flow backward or get clogged.
- Need for explicit blocking/routing mechanisms (junctions, gates, armored conveyors that reject side input).
- Generally found in simpler or tower-defense-hybrid games where logistics is secondary to combat.

---

## Part 3: Comparative Matrix

| Primitive | Factorio | Satisfactory | DSP | Techtonica | Foundry | Mindustry | AK:Endfield | Shapez 2 | Captain of Industry |
|-----------|---------|-------------|-----|-----------|---------|-----------|------------|---------|-------------------|
| **Conveyor Belt** | ✓ (2-lane) | ✓ (single) | ✓ (single) | ✓ (single) | ✓ (single) | ✓ (single) | ✓ (single) | ✓ (single) | ✓ (single, 2 shapes) |
| **Belt Tiers** | 4 | 6 | 3 | 3 | 3+ | 3 | ~2 | Upgradeable | 3 |
| **Inserter/Arm** | ✓ (6+ types) | ✗ | ✓ (Sorter, 3 tiers) | ✓ (7 types) | ✓ (Loader) | ✗ | ✗ | ✗ | ✗ |
| **Direct Port** | ✗ | ✓ | ✗ | ✗ | ✗ (via loader) | ~✓ | ✓ | ✓ | ✓ |
| **Splitter** | ✓ (2→2) | ✓ (1→3) | ✓ (4-port omni) | ✓ (auto) | ✓ (Balancer 2→2) | ✓ (Router, Sorter) | ✓ | ✓ (1→3) | ✓ (8-port Balancer) |
| **Merger** | ~(sideload) | ✓ (3→1) | ✓ (via splitter) | ✓ (auto) | ✓ (via Balancer) | ~(belt join) | ✓ (Converger) | ✓ (3→1) | ✓ (via Balancer) |
| **Underground/Bridge** | ✓ | ✗ (uses 3D) | ✗ (uses height) | ✗ (uses vertical) | ✗ (uses 3D) | ✓ (Bridge, Junction) | ✓ (Bridge) | ✓ (Tunnels) | ✗ (uses 3D) |
| **Vertical Lift** | ✗ (2D) | ✓ | ~(belt height) | ✓ | ✓ (Elevator) | ✗ (2D) | ✗ | ✓ | ~(pillar height) |
| **Storage** | ✓ (Chests) | ✓ (Containers) | ✓ (Depot) | ✓ (Container) | ✓ (Logistic Container) | ✓ (Vault, Container) | ✓ (PAC Depot) | ✗ (Storage block) | ✓ (Various) |
| **Filter Sorting** | ✓ (inserter+splitter) | ✓ (Smart Splitter) | ✓ (Sorter+Splitter) | ✓ (Filter Inserter) | ✓ (Filter Loader) | ✓ (Sorter block) | ✓ (Control Port) | ✗ | ✓ (Flat Sorter) |
| **Logistics Bots/Drones** | ✓ (late-game) | ✗ | ✓ (PLS/ILS drones+vessels) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ (trucks instead) |
| **Trains** | ✓ | ✓ | ✗ | ✗ (Monorail) | ✗ | ✗ | ✗ | ✓ (Space trains) | ✓ (Rail network) |
| **Trucks/Vehicles** | ✓ (car, tank) | ✓ (Tractor, Truck, Drone) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ (Trucks, core logistics) |

---

## Part 4: Notable Subpatterns and Design Insights

### 4.1 The "Port Directionality" Spectrum

Games exist on a spectrum from "fully undirected" to "rigidly directed" in how machines interface with belts:

1. **Undirected / Omni**: Mindustry — buildings accept from any side, output to any side. Maximum flexibility, maximum chaos.
2. **Arm-mediated / Flexible**: Factorio — machines have no inherent port direction; inserters can be placed on any side and aimed in any direction. Very flexible but requires inserters for every transfer.
3. **Arm-mediated / Ported**: DSP, Techtonica — machines have designated port positions, but sorters/inserters still mediate the transfer. Moderate flexibility.
4. **Direct / Fixed ports**: Satisfactory, Arknights: Endfield, Shapez 2 — machines have fixed input and output ports at specific positions. Belt connects directly. Zero flexibility in port placement but zero setup overhead per machine.

This progression correlates with complexity: games toward the "undirected" end tend to be simpler/more chaotic, while games toward the "fixed port" end tend to be more structured and predictable.

### 4.2 Splitter Topology

Three distinct models for how splitters handle port assignment:

- **Fixed asymmetric** (Satisfactory): 1 input → 3 outputs. You always know which side is which. Separate merger building for the inverse. Clean, but requires two distinct building types.
- **Fixed symmetric** (Factorio): 2 inputs → 2 outputs, always. Acts as both splitter and merger depending on what you connect. Elegant and minimal.
- **Omni-assignable** (DSP, Captain of Industry): N ports where any can be input or output based on what's connected. Most flexible, but can be confusing. DSP's splitter has 3 mode configurations. Captain of Industry's Balancer has 8 ports.

### 4.3 The "Crossing Problem" and Its Solutions

When two belt paths need to cross:

- **2D games** MUST provide an explicit mechanism: Factorio uses underground belts, Mindustry uses junctions and bridges, Shapez uses tunnels.
- **3D games** solve this "for free" with elevation changes: Satisfactory, DSP, Foundry, and Techtonica all allow belts at different heights. Some (Arknights: Endfield) still provide bridges as a convenience.
- **Factorio's underground braiding** is unique: different-tier underground belts can overlap on the same tiles, enabling extremely dense belt routing that has no equivalent in other games.

### 4.4 The Filter Placement Question

Where does item filtering happen?

- **At the arm**: Factorio, DSP, Techtonica — the inserter/sorter itself decides what to pick up. This means filtering is distributed across many small entities.
- **At the splitter**: Satisfactory, Captain of Industry — the splitter decides which output gets which item. Filtering is centralized at routing nodes.
- **At dedicated blocks**: Mindustry — standalone sorter and gate blocks sit inline on belts. Arknights: Endfield uses Control Ports.
- **Both**: Factorio and DSP allow filtering at both the arm level and the splitter level, giving the most options.

### 4.5 Centralized vs. Distributed Storage

- **Distributed**: Factorio, Satisfactory, Foundry — storage is local (place chests/containers wherever you need them). Items must be physically moved to/from storage by belts or robots.
- **Centralized**: Arknights: Endfield — the PAC has a central depot that all mining rigs feed into wirelessly. Production draws from and returns to this central pool. This dramatically simplifies logistics but reduces the belt-routing puzzle.
- **Hybrid**: DSP — local depots exist, but Planetary/Interstellar Logistics Stations act as smart hubs that request/supply items via drones across the planet or galaxy, effectively creating a wireless logistics overlay on top of the belt network. Captain of Industry uses trucks as the "wireless" layer.

### 4.6 The "Sushi Belt" Problem

When multiple item types share a belt:

- **Factorio**: Two-lane belts naturally support 2 items. Sushi belts (mixed items on one belt) are possible but require careful circuit network control to prevent clogging.
- **DSP**: Sushi belts are possible but tricky. No circuit network exists, so balance must be achieved through splitter/sorter tricks and rate-matching. The community has developed elaborate designs using box-on-splitter buffers.
- **Satisfactory**: Smart Splitters can filter mixed belts, but the game generally encourages one item type per belt.
- **Mindustry**: Mixed belts are common and handled by inline sorter blocks. The sorter passes matching items straight and routes others sideways.
- **Techtonica**: Filter Inserters extract specific items from mixed belts.

### 4.7 Late-Game Logistics Paradigm Shifts

Many games introduce a fundamentally different logistics mode in the late game:

- **Factorio**: Logistics robots (flying bots that move items between logistic chests) effectively bypass belts for certain workflows. Trains replace belts for long-distance transport.
- **DSP**: Planetary Logistics Stations with drones replace belt-based factories for mid/late game. Interstellar Logistics Stations extend this across star systems.
- **Satisfactory**: Trains for long-distance; Drone Ports for point-to-point delivery.
- **Captain of Industry**: Truck-based logistics is the primary mode from the start; belts are an optimization for high-throughput routes.
- **Arknights: Endfield**: Sub-PACs extend the factory network to new regions, sharing storage with the main PAC.

---

## Part 5: Summary — What's Universal, What's Distinctive

### Universal (found in virtually every factory game):
1. Directional conveyor belts with speed tiers
2. Some form of storage container
3. Some mechanism to split one belt into multiple
4. Some mechanism to merge multiple belts into one

### Very Common (~70-85%):
5. A belt-crossing mechanism (underground, bridge, junction, or 3D elevation)
6. Item filtering at some point in the logistics chain
7. Multiple machine types with distinct production recipes

### Genre-Defining Split:
8. **Inserter/Arm games** vs. **Direct-Port games** — this is the single biggest design axis in the genre. It determines whether the player's primary logistics puzzle is "how do I load/unload each machine efficiently" (inserter games) or "how do I route belts between fixed connection points" (direct-port games).

### Notable Rarities:
- Factorio's two-lane belts (unique in surveyed games)
- DSP's omnidirectional splitter with Depot stacking
- Mindustry's router/junction system and lack of explicit splitter/merger distinction
- Captain of Industry's conveyor shape types (Flat for units, U-shape for loose products)
- Arknights: Endfield's wireless mining-to-depot system
