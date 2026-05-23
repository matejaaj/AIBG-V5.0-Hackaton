"""Monster Hunt Bot — write your tactics here.

Usage: python bot_template_py.py <server_url> <game_id> <bot_name>

Edit decide(bot) below. Everything else lives in helper modules:
  - game_client.py : the Bot (server I/O + actions)
  - game_api.py    : Field/ItemType/Status, Item/Entity/World, tile predicates
  - pathfinding.py : find_path / reachable / find_nearest / distance

The vocabulary available inside decide():

    me, enemy = bot.me, bot.enemy          # Entity: .health .position .max_move ...
    bot.map                                 # World: tile + entity queries

    find_path(me, goal)                     # best tile to MOVE to this turn
    reachable(me, turns=1)                  # set of standable tiles
    find_nearest(me, is_pickup)             # nearest tile matching a predicate
    distance(me, enemy)                     # shortest-path movement cost

    bot.move(pos) / bot.move_toward(goal)
    bot.attack(target) / bot.can_attack(target)
    bot.use(item) / bot.use_healing() / bot.has_item(ItemType.HEALING)
    bot.pickup(pos) / bot.pickup_card(pos) / bot.summon(card, pos)

See README.md for the raw HTTP API.
"""
import os
import sys
import time
from pathlib import Path

# Flush each line so the host (BotProcessService) captures our output promptly.
try:
    sys.stdout.reconfigure(line_buffering=True)
except Exception:
    pass

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from game_client import Bot  # noqa: E402
from game_api import (  # noqa: E402
    Field, ItemType, Status, Entity, Item,
    is_pickup, is_healing, is_card, is_powerup, tile_position,
)
from pathfinding import find_path, reachable, find_nearest, distance  # noqa: E402

# How far off the straight line to center we'll detour for a pickup (movement cost).
DETOUR_MAX = 2

# Set BOT_DEBUG=1 to log a snapshot + the chosen action each turn and dump the
# latest game state to bot_state.json (send that file + this log for debugging).
DEBUG = os.environ.get("BOT_DEBUG", "") not in ("", "0", "false", "False")


def log(*args):
    """Debug print (gated by BOT_DEBUG), flushed so the host captures it."""
    if DEBUG:
        print("[BOT]", *args, flush=True)


# ── Strategy: write your if-else tactics here ──────────────────────────────
def decide(bot):
    """Hold-the-center tactic. Phase A: travel to the never-shrunk zone, grabbing
    pickups that are roughly on the way. Phase B: stay inside the zone and only
    act within it (the map shrinks 3x and kills anyone caught outside)."""
    me, enemy = bot.me, bot.enemy
    if me is None:
        return
    world = bot.map
    center = world.center()
    safe = world.is_permanently_safe

    # Heal when low (any phase).
    if me.health <= me.max_health * 0.4 and bot.healing_item():
        log("action: heal (low health)")
        bot.use_healing()
        return

    def grab_or_approach(target, within=None):
        """Pick the target up if adjacent, else move toward it. Returns True if
        we performed an action (the server has no 'pass', so callers rely on this)."""
        if bot.is_adjacent(me, target):
            log("action: pickup", target)
            tile = world.tile_at(target)
            (bot.pickup_card if is_card(tile) else bot.pickup)(target)
            return True
        log("action: approach pickup", target)
        return bool(bot.move_toward(target, within=within))

    # ── PHASE A: travel to center, with small on-path detours for pickups ──
    if not safe(me.position):
        pickup = find_nearest(me, is_pickup)
        if pickup is not None:
            base = distance(me, center) or 0
            detour = (distance(me, pickup) or 1e9) + (distance(pickup, center) or 1e9) - base
            if detour <= DETOUR_MAX and grab_or_approach(pickup):   # barely off the path
                return
        log("action: travel to center", center)
        bot.move_toward(center) or bot.wander()    # always act — never freeze
        return

    # ── PHASE B: hold the zone — act only inside it, never leave ──
    if enemy and bot.can_attack(enemy) and safe(enemy.position):
        log("action: attack enemy", enemy.position)
        bot.attack(enemy)
        return

    target = find_nearest(me, lambda t: is_pickup(t) and safe(tile_position(t)))
    if target and grab_or_approach(target, within=safe):
        return

    if enemy and safe(enemy.position):         # enemy is in our zone — close in
        log("action: close on enemy", enemy.position)
        if bot.move_toward(enemy, within=safe):
            return

    # The server has no "pass": always submit a move. Drift toward center (a
    # harmless shuffle once we're there); if boxed in, take any legal safe move.
    log("action: hold/shuffle toward center", center)
    bot.move_toward(center, within=safe) or bot.wander(within=safe) or bot.wander()


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python bot_template_py.py <server_url> <game_id> <bot_name>")
        sys.exit(1)

    bot = Bot(sys.argv[1], sys.argv[2], sys.argv[3])

    while not (state := bot.get_game_state()) or not bot.find_my_player_id(state):
        time.sleep(0.5)

    print(f"Connected as Player {bot.player_id}\n")

    state = bot.get_game_state()
    try:
        while state and not bot.is_game_over(state):
            if bot.is_my_turn(state):
                bot.update(state)
                if DEBUG:
                    log(bot.snapshot())
                    bot.save_state()        # dump bot_state.json for offline replay
                try:
                    decide(bot)
                except Exception as e:  # never let one bad turn kill the bot
                    print("decide() error:", e, flush=True)
                    bot.save_state("bot_error_state.json")
                # Act once per turn: wait for the server to advance past our turn
                # before deciding again (avoids a 409 from a second action).
                while state and not bot.is_game_over(state) and bot.is_my_turn(state):
                    time.sleep(0.2)
                    state = bot.get_game_state()
            else:
                time.sleep(0.2)
                state = bot.get_game_state()
    except KeyboardInterrupt:
        print("\nBot stopped by user")
