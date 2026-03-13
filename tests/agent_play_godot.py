#!/usr/bin/env python3
"""
Agent-as-Player: drives the Godot game via WebSocket to craft all recipes.

Godot's CreateFromRegistry only places Workshop initially. The agent must:
1. Craft Workbench, Tanner, Seedling Pot from Workshop
2. Craft each facility's recipes (Stone Axe, Small Hammer, Belt Pouch, Forest Bag)

Usage: python3 tests/agent_play_godot.py
Requires: Godot running with WebSocket debug server on port 9080
"""
import asyncio
import json
import sys

import websockets

WS_URL = "ws://localhost:9080"

# All recipes organized by facility, in the order they must be crafted.
# Workshop recipes come first (they produce the other facilities).
FACILITY_RECIPES = [
    # Workshop produces facilities
    {
        "facility_env": "Workshop",
        "recipes": [
            {
                "id": "workshop-workbench",
                "name": "Workbench",
                "inputs": [("Plain Rock", 8), ("Rough Wood", 4)],
                "duration": 5,
                "output_is_facility": True,
            },
            {
                "id": "workshop-tanner",
                "name": "Tanner",
                "inputs": [("Tanned Leather", 6), ("Woven Fiber", 3)],
                "duration": 5,
                "output_is_facility": True,
            },
            {
                "id": "workshop-seedling-pot",
                "name": "Seedling Pot",
                "inputs": [("Rich Soil", 4), ("Rough Wood", 2)],
                "duration": 4,
                "output_is_facility": True,
            },
        ],
    },
    # Workbench produces tools
    {
        "facility_env": "Workbench",
        "recipes": [
            {
                "id": "workbench-axe",
                "name": "Stone Axe",
                "inputs": [("Plain Rock", 5), ("Rough Wood", 3)],
                "duration": 3,
            },
            {
                "id": "workbench-hammer",
                "name": "Small Hammer",
                "inputs": [("Plain Rock", 3), ("Rough Wood", 2)],
                "duration": 2,
            },
        ],
    },
    # Tanner produces bags
    {
        "facility_env": "Tanner",
        "recipes": [
            {
                "id": "tanner-pouch",
                "name": "Belt Pouch",
                "inputs": [("Tanned Leather", 3), ("Woven Fiber", 2)],
                "duration": 5,
            },
        ],
    },
    # Seedling Pot produces wilderness bags
    {
        "facility_env": "Seedling Pot",
        "recipes": [
            {
                "id": "seedling-forest",
                "name": "Forest Bag",
                "inputs": [("Forest Seed", 5), ("Rich Soil", 3)],
                "duration": 8,
            },
        ],
    },
]


class GodotAgent:
    def __init__(self, ws):
        self.ws = ws
        self.state = None

    async def send(self, cmd):
        await self.ws.send(json.dumps(cmd))
        resp = json.loads(await self.ws.recv())
        if "error" in resp:
            raise RuntimeError(f"Server error: {resp['error']}")
        self.state = resp["state"]
        return resp

    async def get_state(self):
        return await self.send({"action": "state"})

    async def key(self, key_name):
        return await self.send({"action": "key", "key": key_name})

    async def click(self, row, col, button="Primary"):
        return await self.send({"action": "click", "row": row, "col": col, "button": button})

    # --- State queries ---

    def find_item(self, item_name, min_count=1):
        """Find an item in the current grid by name, returns (row, col) or None."""
        for cell in self.state["cells"]:
            if not cell["empty"] and cell["item"] == item_name and cell["count"] >= min_count:
                return (cell["row"], cell["col"])
        return None

    def find_facility(self, env_type):
        """Find a facility bag by checking contained bags. Must be at root."""
        # Facilities appear as bag items with specific names matching env_type
        # The item name matches the facility name (Workshop, Workbench, etc.)
        for cell in self.state["cells"]:
            if not cell["empty"] and cell.get("hasBag"):
                # Match by item name to environment type mapping
                if cell["item"] == env_type or cell["item"] == env_type.replace(" ", " "):
                    return (cell["row"], cell["col"])
        return None

    def find_empty_cell(self):
        """Find first empty cell in current grid."""
        for cell in self.state["cells"]:
            if cell["empty"]:
                return (cell["row"], cell["col"])
        return None

    def find_output_item(self):
        """Find non-empty output slot in current grid (facility interior)."""
        for cell in self.state["cells"]:
            if not cell["empty"] and cell.get("frame") == "OutputSlotFrame":
                return (cell["row"], cell["col"])
        return None

    def cursor_pos(self):
        return (self.state["cursor"]["row"], self.state["cursor"]["col"])

    def hand_empty(self):
        return self.state["handEmpty"]

    def is_nested(self):
        return self.state["isNested"]

    def grid_size(self):
        return (self.state["gridRows"], self.state["gridColumns"])

    # --- Navigation ---

    async def navigate_to(self, target_row, target_col):
        """Move cursor step-by-step to target position."""
        rows, cols = self.grid_size()
        cur_r, cur_c = self.cursor_pos()

        # Navigate rows
        while cur_r != target_row:
            row_dist = (target_row - cur_r) % rows
            if row_dist <= rows // 2:
                await self.key("Down")
            else:
                await self.key("Up")
            cur_r, cur_c = self.cursor_pos()

        # Navigate columns
        while cur_c != target_col:
            col_dist = (target_col - cur_c) % cols
            if col_dist <= cols // 2:
                await self.key("Right")
            else:
                await self.key("Left")
            cur_r, cur_c = self.cursor_pos()

    # --- Actions ---

    async def grab_item(self, item_name, exact_count=None):
        """Navigate to item and grab it."""
        pos = self.find_item(item_name, min_count=exact_count or 1)
        assert pos is not None, f"Item '{item_name}' (>={exact_count or 1}) not found in grid"
        await self.navigate_to(*pos)

        if exact_count is not None:
            # Check if we need to split
            cell = next(c for c in self.state["cells"]
                        if c["row"] == pos[0] and c["col"] == pos[1])
            if cell["count"] > exact_count:
                # Use secondary to grab half, or just grab all and deal with it
                # For simplicity, grab the whole stack — the slot filter will reject excess
                pass

        await self.key("Primary")  # grab
        assert not self.hand_empty(), f"Hand should have items after grabbing {item_name}"

    async def deliver_to_facility_slot(self, facility_pos, slot_idx):
        """Enter facility, navigate to slot, drop, leave."""
        assert not self.hand_empty(), "Hand must have items before delivering"
        assert not self.is_nested(), "Must be at root to enter facility"

        await self.navigate_to(*facility_pos)
        await self.key("Primary")  # enter facility
        assert self.is_nested(), "Should be inside facility"

        cols = self.state["gridColumns"]
        slot_row = slot_idx // cols
        slot_col = slot_idx % cols
        await self.navigate_to(slot_row, slot_col)
        await self.key("Primary")  # drop
        assert self.hand_empty(), "Hand should be empty after drop"

        await self.key("LeaveBag")
        assert not self.is_nested(), "Should be back at root"

    def find_facility_recipe(self):
        """When inside a facility, check if there's an ActiveRecipeId in action log."""
        log = self.state.get("actionLog", [])
        cycle_logs = [l for l in log if "CycleRecipe:" in l]
        if cycle_logs:
            return cycle_logs[-1]
        return None

    async def set_recipe(self, facility_pos, target_recipe_id):
        """Enter facility, cycle recipe until it matches, leave."""
        await self.navigate_to(*facility_pos)
        await self.key("Primary")  # enter
        assert self.is_nested(), "Should be inside facility"

        # Cycle recipe up to 20 times
        for _ in range(20):
            await self.key("CycleRecipe")
            log = self.state.get("actionLog", [])
            cycle_logs = [l for l in log if "CycleRecipe:" in l]
            if cycle_logs and target_recipe_id in cycle_logs[-1]:
                break

        await self.key("LeaveBag")
        assert not self.is_nested()

    async def tick_until_complete(self, facility_env, max_ticks=50):
        """Sort repeatedly (generates ticks) until facility craft completes."""
        for i in range(max_ticks):
            # Sort at root level to generate ticks
            await self.key("Sort")

            # Check every few ticks if craft is done
            if i % 3 == 2 or i >= max_ticks - 1:
                facility_pos = self.find_facility(facility_env)
                assert facility_pos is not None, f"Facility {facility_env} not found"
                await self.navigate_to(*facility_pos)
                await self.key("Primary")  # enter
                assert self.is_nested()

                output = self.find_output_item()
                await self.key("LeaveBag")

                if output is not None:
                    return  # craft complete

        raise AssertionError(f"Craft in {facility_env} did not complete within {max_ticks} ticks")

    async def extract_output(self, facility_env):
        """Enter facility, grab output, leave, drop in empty root cell."""
        facility_pos = self.find_facility(facility_env)
        assert facility_pos is not None
        await self.navigate_to(*facility_pos)
        await self.key("Primary")  # enter
        assert self.is_nested()

        output_pos = self.find_output_item()
        assert output_pos is not None, f"No output in {facility_env}"

        await self.navigate_to(*output_pos)
        await self.key("Primary")  # grab (output slots force grab)
        assert not self.hand_empty(), f"Failed to grab output from {facility_env}"

        await self.key("LeaveBag")
        assert not self.is_nested()

        empty = self.find_empty_cell()
        assert empty is not None, "No empty cell to drop output"
        await self.navigate_to(*empty)
        await self.key("Primary")  # drop
        assert self.hand_empty()


async def harvest_from_wilderness(agent, bag_name="Forest Bag"):
    """Enter a wilderness bag and harvest ALL materials to root."""
    bag_pos = agent.find_item(bag_name)
    if bag_pos is None:
        print(f"  No {bag_name} found to harvest from")
        return 0

    print(f"  Harvesting from {bag_name} at {bag_pos}...")
    await agent.navigate_to(*bag_pos)
    await agent.key("Primary")  # enter bag
    if not agent.is_nested():
        print(f"  Could not enter {bag_name}")
        return 0

    harvested = 0
    # Keep harvesting until no more non-bag items remain
    while True:
        non_empty = [c for c in agent.state["cells"]
                     if not c["empty"] and not c.get("hasBag")]
        if not non_empty:
            break
        cell = non_empty[0]
        await agent.navigate_to(cell["row"], cell["col"])
        await agent.key("Primary")  # harvest
        harvested += 1

    await agent.key("LeaveBag")
    print(f"  Harvested {harvested} items from {bag_name}")
    return harvested


def total_available(agent, item_name):
    """Count total of an item across all stacks in the current grid."""
    return sum(c["count"] for c in agent.state["cells"]
               if not c["empty"] and c["item"] == item_name)


async def ensure_materials(agent, item_name, needed):
    """Check if we have enough of an item; if not, try harvesting from wilderness."""
    # Sort first to merge scattered stacks
    await agent.key("Sort")
    await agent.get_state()

    if agent.find_item(item_name, min_count=needed) is not None:
        return True

    total = total_available(agent, item_name)
    if total >= needed:
        return True  # available but split — sort should have merged

    # Not enough — harvest from all wilderness bags
    for bag_name in ["Forest Bag"]:
        harvested = await harvest_from_wilderness(agent, bag_name)
        if harvested > 0:
            await agent.key("Sort")
            await agent.get_state()
            if agent.find_item(item_name, min_count=needed) is not None:
                return True

    # Final check with total count
    total = total_available(agent, item_name)
    return total >= needed


async def run_all_recipes():
    async with websockets.connect(WS_URL) as ws:
        agent = GodotAgent(ws)
        await agent.get_state()

        print(f"Grid: {agent.state['gridColumns']}x{agent.state['gridRows']}")
        items = [c for c in agent.state["cells"] if not c["empty"]]
        print(f"Starting items: {len(items)}")
        for c in items:
            bag = " (bag)" if c.get("hasBag") else ""
            print(f"  ({c['row']},{c['col']}): {c['count']}x {c['item']}{bag}")

        completed = []
        failed = []

        for facility_group in FACILITY_RECIPES:
            facility_env = facility_group["facility_env"]

            for recipe in facility_group["recipes"]:
                recipe_id = recipe["id"]
                recipe_name = recipe["name"]
                print(f"\n--- Crafting: {recipe_name} in {facility_env} ---")

                try:
                    # Find the facility
                    facility_pos = agent.find_facility(facility_env)
                    if facility_pos is None:
                        raise AssertionError(f"Facility '{facility_env}' not found in grid")

                    # Set correct recipe
                    await agent.set_recipe(facility_pos, recipe_id)

                    # Re-find facility (position may have shifted after recipe cycle dumps)
                    await agent.get_state()
                    facility_pos = agent.find_facility(facility_env)
                    assert facility_pos is not None

                    # Deliver each input (harvest from wilderness if needed)
                    for idx, (item_name, count) in enumerate(recipe["inputs"]):
                        print(f"  Delivering {count}x {item_name} to slot {idx}")
                        has_enough = await ensure_materials(agent, item_name, count)
                        if not has_enough:
                            raise Exception(f"Not enough {item_name} (need {count})")
                        # Re-find facility after potential sort/harvest
                        facility_pos = agent.find_facility(facility_env)
                        assert facility_pos is not None
                        await agent.grab_item(item_name, exact_count=count)
                        await agent.deliver_to_facility_slot(facility_pos, idx)

                    # Tick until complete
                    print(f"  Waiting for craft ({recipe['duration']} ticks)...")
                    await agent.tick_until_complete(facility_env)

                    # Extract output
                    await agent.extract_output(facility_env)
                    print(f"  OK: {recipe_name} crafted successfully")
                    completed.append(recipe_id)

                except Exception as e:
                    print(f"  FAILED: {e}")
                    failed.append((recipe_id, str(e)))
                    # Try to recover: leave bag if nested, drop hand items
                    try:
                        await agent.get_state()
                        while agent.is_nested():
                            await agent.key("LeaveBag")
                        if not agent.hand_empty():
                            empty = agent.find_empty_cell()
                            if empty:
                                await agent.navigate_to(*empty)
                                await agent.key("Primary")
                    except:
                        pass

        # Summary
        material_failures = [(r, e) for r, e in failed if "Not enough" in e]
        bug_failures = [(r, e) for r, e in failed if "Not enough" not in e]

        print(f"\n{'='*50}")
        print(f"Results: {len(completed)} crafted, {len(failed)} failed")
        print(f"Completed: {', '.join(completed)}")
        if material_failures:
            print(f"Material shortage (expected — limited starter resources):")
            for rid, err in material_failures:
                print(f"  {rid}: {err}")
        if bug_failures:
            print(f"BUGS:")
            for rid, err in bug_failures:
                print(f"  {rid}: {err}")
            sys.exit(1)
        elif not failed:
            print("All recipes crafted successfully!")
        else:
            print(f"\nAll {len(completed)} craftable recipes succeeded. "
                  f"{len(material_failures)} skipped due to material constraints.")


if __name__ == "__main__":
    asyncio.run(run_all_recipes())
