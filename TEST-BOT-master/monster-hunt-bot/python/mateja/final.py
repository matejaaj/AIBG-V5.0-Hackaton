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
from datetime import datetime
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
    is_pickup, tile_position,
)
from pathfinding import find_nearest  # noqa: E402
from strategy_helpers import grab_or_approach, summon_monster, pickup_on_route  # noqa: E402

# Debug is ON by default (the GUI launcher can't set env vars): logs a snapshot +
# the chosen action each turn and dumps the latest state to bot_state.json.
# Set BOT_DEBUG=0 to silence it (e.g. for competition).
DEBUG = os.environ.get("BOT_DEBUG", "1") not in ("0", "false", "False")
DEBUG_LOG = CURRENT_DIR / f"bot_debug_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"
if DEBUG:
    try:
        DEBUG_LOG.write_text("")            # truncate once per run
    except OSError:
        pass


def log(*args):
    """Debug print (gated by BOT_DEBUG): to stdout (captured by the host) AND to
    bot_debug.log next to the script, so it's easy to find and send."""
    if not DEBUG:
        return
    line = "[BOT] " + " ".join(str(a) for a in args)
    print(line, flush=True)
    try:
        with open(DEBUG_LOG, "a") as f:
            f.write(line + "\n")
    except OSError:
        pass


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

    # ── PHASE A: travel to center, with small on-path detours for pickups ──
    if not safe(me.position):
        pickup = find_nearest(me, lambda t: is_pickup(t) and pickup_on_route(t))
        if pickup is not None and grab_or_approach(pickup):
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

    if summon_monster(within=safe):
        return

    # The server has no "pass": always submit a move. Drift toward center (a
    # harmless shuffle once we're there); if boxed in, take any legal safe move.
    log("action: hold/shuffle toward center", center)
    bot.move_toward(center, within=safe) or bot.wander(within=safe) or bot.wander()


def _turn_key(state):
    """Identity of a turn — used to detect when our action didn't advance the game."""
    return (state.get("GameState"), state.get("TurnCounter")) if state else None


def run(bot, poll=1.0, pace=0.2):
    """Main turn loop. Act once when it's our turn, then poll until our next turn.
    `pace` throttles every action so we never hammer the server."""
    state = bot.get_game_state()
    last = time.monotonic()
    while state and not bot.is_game_over(state):
        if bot.is_my_turn(state):
            bot.update(state)
            now = time.monotonic()
            if DEBUG:
                log(f"+{now - last:.2f}s", bot.snapshot())   # real gap since our last turn
                bot.save_state()            # dump bot_state.json for offline replay
            last = now
            try:
                decide(bot)
            except Exception as e:          # never let one bad turn kill the bot
                print("decide() error:", e, flush=True)
                bot.save_state("bot_error_state.json")
            time.sleep(pace)                 # pace the game + never hammer the server
            state = bot.get_game_state()     # turn normally advanced → now the opponent's
        else:
            time.sleep(poll)
            state = bot.get_game_state()


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python bot_template_py.py <server_url> <game_id> <bot_name>")
        sys.exit(1)

    bot = Bot(sys.argv[1], sys.argv[2], sys.argv[3])

    print(f"[BOT] running {os.path.abspath(__file__)} | "
          f"pace=0.2 poll=1.0 | DEBUG={DEBUG}", flush=True)

    while not (state := bot.get_game_state()) or not bot.find_my_player_id(state):
        time.sleep(0.5)

    print(f"Connected as Player {bot.player_id}", flush=True)

    try:
        run(bot)
    except KeyboardInterrupt:
        print("\nBot stopped by user")
