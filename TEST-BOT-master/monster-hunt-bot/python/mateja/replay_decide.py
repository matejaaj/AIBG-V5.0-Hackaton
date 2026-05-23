"""Replay decide() against a saved game state — no server needed.

Usage: python replay_decide.py bot_state.json <botName>

Loads a state dumped by the bot (BOT_DEBUG=1 writes bot_state.json each turn),
runs decide() with the network actions stubbed out, and prints the snapshot plus
the single action the bot would take — or "*** NONE ***" if it would freeze.
"""
import json
import sys

from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from pathfinding import _pos  # noqa: E402
from game_client import Bot  # noqa: E402
import working_dir.final as tmpl  # noqa: E402


def main():
    if len(sys.argv) < 3:
        print("Usage: python replay_decide.py <state.json> <botName>")
        return
    path, name = sys.argv[1], sys.argv[2]
    with open(path) as f:
        state = json.load(f)

    bot = Bot("http://replay", "game", name)
    if not bot.find_my_player_id(state):
        players = [p.get('Name') for p in (state.get('Players', {}) or {}).values()]
        print(f"Bot named {name!r} not found. Players in state: {players}")
        return
    bot.update(state)
    print("STATE:", bot.snapshot())

    # Record actions instead of sending them over HTTP.
    actions = []
    bot.move = lambda p: actions.append(("move", tuple(_pos(p)))) or True
    bot.attack = lambda t: actions.append(("attack", getattr(t, "position", t))) or True
    bot.pickup = lambda t: actions.append(("pickup", t if isinstance(t, tuple) else "tile")) or True
    bot.pickup_card = lambda t: actions.append(("pickup_card", t if isinstance(t, tuple) else "tile")) or True
    bot.use = lambda i: actions.append(("use_item", getattr(i, "id", i))) or True

    tmpl.decide(bot)
    print("ACTION:", actions[0] if actions else "*** NONE (would freeze) ***")
    if len(actions) > 1:
        print("WARNING: decide() produced multiple actions:", actions)


if __name__ == "__main__":
    main()
