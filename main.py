"""Monster Hunt bot entry point: turn loop and the decide() tactics."""
import os
import sys
import time
from datetime import datetime
from pathlib import Path

# Flush each line so the host (BotProcessService) captures our output promptly.
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)
except Exception:
    pass

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from game_client import Bot  # noqa: E402
from game_api import (  # noqa: E402
    Field, ItemType, Status, Entity, Item,
    is_pickup, is_card, is_healing, tile_position,
)
from pathfinding import find_nearest  # noqa: E402
from strategy_helpers import grab_or_approach, summon_monster, can_afford_pickup  # noqa: E402

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
    try:
        sys.stdout.buffer.write((line + "\n").encode("utf-8", errors="replace"))
        sys.stdout.buffer.flush()
    except AttributeError:
        print(line, flush=True)
    try:
        with open(DEBUG_LOG, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except OSError:
        pass


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

    # Effective HP: current health + healing value of all potions in inventory.
    eff_hp = me.health + sum(
        (it.power or 0) for it in me.inventory if it.item_type == ItemType.HEALING
    )
    # Max spike damage anywhere on the map (read once per turn).
    _max_spike = 0
    for _tile in world._grid:
        if isinstance(_tile, dict):
            _obs = _tile.get('Obstacle') or _tile.get('obstacle')
            if isinstance(_obs, dict):
                _dmg = int(_obs.get('Damage') or _obs.get('damage') or 0)
                if _dmg > _max_spike:
                    _max_spike = _dmg
    # Cross spikes only when already in the permanently-safe zone AND effective
    # HP exceeds the worst single-tile spike damage.
    allow_spikes = safe(me.position) and _max_spike > 0 and eff_hp > _max_spike
    avoid_spikes = not allow_spikes
    if allow_spikes:
        log(f"spike-crossing ON (eff_hp={eff_hp} > max_spike={_max_spike})")

    # Log any active status effects so they're visible at the top of each turn.
    if me.statuses:
        log("statuses:", dict(me.statuses))

    # Heal when low (any phase).
    if me.health <= me.max_health * 0.4 and bot.healing_item():
        log("action: heal (low health)")
        bot.use_healing()
        return

    # ── Item inventory full: proactively use one to free a slot ─────────────
    # Priority: heal (if not at full HP) → freeze.
    # Dizzy is intentionally never used — it is unreliable for offense and the
    # anti-invert defense we already have is the correct response to it.
    # Strict tactical conditions (adjacency etc.) do NOT apply here — the goal
    # is purely to open a slot so we can pick up something better.
    if len(me.inventory) >= 3:
        if bot.healing_item() and me.health < me.max_health:
            log("action: heal (inventory full — freeing slot + healing)")
            bot.use_healing()
            return
        _freeze = bot.find_item(ItemType.CROWD_CONTROL)
        if _freeze and enemy and not enemy.has_status("Frozen"):
            log("action: freeze enemy (inventory full — freeing slot)", enemy.position)
            bot.use(_freeze)
            return
        # Nothing usable (heal at full HP, freeze already applied, or only Dizzy left)
        # → inventory stays full; want_pickup will block new grabs

    # ── want_pickup: only target tiles we still have room for ───────────────
    # Dizzy (MOVEMENT type) is intentionally skipped — we never pick it up.
    def want_pickup(t):
        if is_card(t):
            return len(me.cards) < 3        # card cap
        if not is_pickup(t) or len(me.inventory) >= 3:
            return False
        item = t.get('Item') or t.get('item') if isinstance(t, dict) else None
        if item and (item.get('ItemType') or item.get('itemType')) == ItemType.MOVEMENT:
            return False                    # never pick up Dizzy
        return True

    # ── PHASE A: travel to center, grabbing pickups we can afford ───────────
    # "Affordable" = extra detour cost fits inside our spare time budget before
    # the next map shrink.  Automatically tight late-game, generous early-game.
    if not safe(me.position):
        _turns_left = world.turns_until_shrink()
        _max_move   = me.max_move or 4
        pickup = find_nearest(
            me,
            lambda t: want_pickup(t) and can_afford_pickup(
                me, center, t, _turns_left, _max_move,
            ),
        )
        if pickup is not None and grab_or_approach(bot, me, world, pickup, log):
            return
        log("action: travel to center", center)
        if bot.move_toward(center, avoid_spikes=avoid_spikes) or bot.wander(avoid_spikes=avoid_spikes):
            return
        log("action: travel to center → STUCK (no legal move)")
        return

    # ── PHASE B: hold the zone — act only inside it, never leave ──
    if summon_monster(bot, me, enemy, center, safe, log, within=safe):
        return

    # FREEZE: use before attacking when the enemy would survive a single hit.
    # Frozen skips their entire next turn — guarantees our follow-up attack lands
    # before they can flee, heal, or counter. No range requirement on freeze scroll.
    # We don't check safe(enemy.position) — we attack from our tile, not theirs.
    freeze = bot.find_item(ItemType.CROWD_CONTROL)
    if (freeze and enemy
            and safe(me.position)
            and not enemy.has_status("Frozen")
            and bot.is_adjacent(me, enemy)
            and enemy.health > me.attack_power):
        log("action: freeze enemy (survives 1 hit — locking in place)", enemy.position)
        bot.use(freeze)
        return

    # Attack: enemy player first, then any monsters they summoned.
    # No safe() check on the target's position — we stay on our tile to attack.
    _attack_targets = []
    if enemy:
        _attack_targets.append(enemy)
    for _ent in world.enemies():
        if _ent.id != (enemy.id if enemy else None):
            _attack_targets.append(_ent)
    for _tgt in _attack_targets:
        if bot.can_attack(_tgt):
            log("action: attack", _tgt.name or "entity", _tgt.position)
            bot.attack(_tgt)
            return

    target = find_nearest(me, lambda t: want_pickup(t) and safe(tile_position(t)))
    if target and grab_or_approach(bot, me, world, target, log, within=safe):
        return

    if enemy and safe(enemy.position):         # enemy is in our zone — close in
        log("action: try close on enemy", enemy.position)
        if bot.move_toward(enemy, within=safe, avoid_spikes=avoid_spikes):
            log("action: close on enemy → ok")
            return
        log("action: close on enemy → no move (path blocked)")

    # The server has no "pass": always submit a move. Drift toward center (a
    # harmless shuffle once we're there); if boxed in, take any legal safe move.
    log("action: hold/shuffle toward center", center)
    if (bot.move_toward(center, within=safe, avoid_spikes=avoid_spikes)
            or bot.wander(within=safe, avoid_spikes=avoid_spikes)
            or bot.wander(avoid_spikes=avoid_spikes)):
        return
    log("action: hold/shuffle → STUCK (completely boxed in — turn will time out)"
        f" last_status={bot._last_action_status}")


def _turn_key(state):
    """Identity of a turn — used to detect when our action didn't advance the game."""
    return (state.get("GameState"), state.get("TurnCounter")) if state else None


def run(bot, poll=1.0, pace=0.2):
    """Main turn loop. Act once when it's our turn, then poll until our next turn.
    `pace` throttles every action so we never hammer the server."""
    state = bot.get_game_state()
    last = time.monotonic()
    last_processed_turn = None
    while state and not bot.is_game_over(state):
        turn_key = _turn_key(state)
        if bot.is_my_turn(state):
            if turn_key == last_processed_turn:
                time.sleep(poll)
                state = bot.get_game_state()
                continue
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
            last_processed_turn = turn_key
            time.sleep(pace)                 # pace the game + never hammer the server
            state = bot.get_game_state()     # turn normally advanced → now the opponent's
        else:
            last_processed_turn = None
            time.sleep(poll)
            state = bot.get_game_state()


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python main.py <server_url> <game_id> <bot_name>")
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
