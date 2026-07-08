"""Monster Hunt — Bot client.

Server I/O + game actions. Parses each turn's state into bot.me / bot.enemy /
bot.map (a World) and exposes move / attack / use / pickup / summon helpers that
accept either a position tuple or an Entity/Item.
"""
from pathlib import Path
import json
import sys

import requests

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from pathfinding import find_path, reachable, set_active_map, _pos  # noqa: E402
from game_api import Entity, Item, World, ItemType, _get, _is_item_type  # noqa: E402


class Bot:
    ACTION_TIMEOUT = 35

    def __init__(self, server_url, game_id, bot_name):
        self.server_url = server_url.rstrip('/')
        self.game_id = game_id
        self.bot_name = bot_name
        self.player_id = None
        self.state = None
        self.me = None
        self.enemy = None
        self.map = None
        # Last action response/state and status code (used to avoid extra GETs)
        self._last_action_state = None
        self._last_action_status = None
        # The "No-UI" header marks us as a bot: the server then skips its 2s
        # per-action UI delay and returns immediately instead of long-polling
        # 30s (which otherwise fails with "Other player didn't make a move").
        self._http = requests.Session()
        self._http.headers.update({"No-UI": "1"})

    # ── connection / turn plumbing ─────────────────────────────────────────
    def get_game_state(self):
        url = f"{self.server_url}/game/state/{self.game_id}"
        try:
            resp = self._http.get(url, timeout=5)
        except requests.RequestException:
            return None
        return resp.json() if resp.status_code == 200 else None

    def find_my_player_id(self, state):
        for player in (state.get('Players', {}) or {}).values():
            if _get(player, 'Name', 'name') == self.bot_name:
                self.player_id = _get(player, 'Id', 'id')
                return True
        return False

    def is_my_turn(self, state):
        if not state or self.player_id is None:
            return False
        players = state.get('Players', {}) or {}
        me = players.get(str(self.player_id)) or players.get(self.player_id)
        if not me:
            return False
        is_first = _get(me, 'First', 'first', default=False)
        gs = state.get('GameState', '')
        return (is_first and gs == 'Player1Turn') or (not is_first and gs == 'Player2Turn')

    def is_game_over(self, state):
        return bool(state) and state.get('GameState', '') == 'Ending'

    def update(self, state):
        """Parse the state into me / enemy / map and bind the active map."""
        self.state = state
        players = state.get('Players', {}) or {}
        me_raw = players.get(str(self.player_id)) or players.get(self.player_id)
        enemy_raw = next(
            (p for p in players.values() if _get(p, 'Id', 'id') != self.player_id), None)
        self.me = Entity(me_raw) if me_raw else None
        self.enemy = Entity(enemy_raw) if enemy_raw else None
        map_raw = state.get('Map') or state.get('map') or {}
        enemy_id = self.enemy.id if self.enemy else None
        self.map = World(map_raw, self.player_id, enemy_id, state.get('TurnCounter'))
        set_active_map(map_raw)

    # ── debugging helpers ───────────────────────────────────────────────────
    def snapshot(self):
        """One-line summary of the current turn — log this every turn."""
        from pathfinding import reachable as _reachable
        from game_api import ItemType as _IT
        _abbr = {_IT.HEALING: 'Heal', _IT.MOVEMENT: 'Move', _IT.DEFENSE: 'Def',
                 _IT.UTILITY: 'Util', _IT.CROWD_CONTROL: 'CC', _IT.ATTACK: 'Atk'}
        me, en, w = self.me, self.enemy, self.map
        tc     = self.state.get('TurnCounter', '?') if self.state else '?'
        gs     = self.state.get('GameState',   '?') if self.state else '?'
        here   = w.is_permanently_safe(me.position) if (w and me) else '?'
        d      = w.distance(me.position, en.position) if (w and me and en) else '?'
        dist_s = '∞' if d is None else d
        shrink = w.turns_until_shrink() if w else '?'
        reach  = len(_reachable(me, 1)) if me else '?'

        base = (f"T{tc} {gs} | me={me.position if me else None} "
                f"hp={me.health if me else '?'}/{me.max_health if me else '?'} "
                f"enemy={en.position if en else None} dist={dist_s} "
                f"safe={here} shrink={shrink} reach={reach}")

        extras = []
        if me:
            sts = {k: v for k, v in me.statuses.items() if int(v or 0) > 0}
            if sts:
                extras.append(f"sts={sts}")
            inv = [_abbr.get(it.item_type, it.name or '?') for it in me.inventory]
            if inv:
                extras.append(f"inv=[{','.join(str(i) for i in inv)}]")
            rdy = sum(1 for c in me.cards
                      if not c.get('OnCooldown')
                      and (c.get('Uses') is None or (c.get('Uses') or 0) > 0)
                      and not (c.get('Monster') or {}).get('SummonedByPlayerId'))
            if rdy:
                extras.append(f"cards={rdy}rdy")
        return base + (' | ' + ' '.join(extras) if extras else '')

    def save_state(self, path=None):
        """Dump the latest raw game state to a file (send this for debugging).
        Defaults to a fixed path next to the script, so it's findable even when
        the GUI launches the bot from an unknown working directory."""
        if self.state is None:
            return None
        if path is None:
            path = str(CURRENT_DIR / "bot_state.json")
        elif not Path(path).is_absolute():
            path = str(CURRENT_DIR / path)
        try:
            with open(path, "w") as f:
                json.dump(self.state, f)
        except OSError as e:
            print("save_state failed:", e, flush=True)
        return path

    def _put(self, url, body=None):
        try:
            resp = self._http.put(url, json=body, timeout=self.ACTION_TIMEOUT)
        except requests.RequestException as e:
            print("Request failed:", e)
            self._last_action_state = None
            self._last_action_status = None
            return None
        if resp.status_code != 200:
            print(f"Action rejected ({resp.status_code}): {resp.text[:200]}")
            self._last_action_state = None
            self._last_action_status = resp.status_code
            return None
        try:
            data = resp.json()
            # record the returned canonical game state so the caller can avoid
            # an immediate GET and reduce polling churn
            self._last_action_state = data
            self._last_action_status = resp.status_code
            return data
        except ValueError:
            self._last_action_state = None
            self._last_action_status = resp.status_code
            return None

    # ── actions ────────────────────────────────────────────────────────────
    def is_adjacent(self, a, b):
        ax, ay = _pos(a)
        bx, by = _pos(b)
        return abs(ax - bx) + abs(ay - by) == 1

    def move(self, pos):
        x, y = _pos(pos)
        # Dizzy compensation: the server inverts our movement vector (sends us in the
        # opposite direction), so we pre-invert the destination so we still arrive where
        # we actually want to go.  Formula: anti_target = 2*current - intended_target.
        # The server then does 2*current - anti_target = intended_target ✓.
        if self.me and self.me.has_status("Dizzy"):
            mx, my = self.me.position
            map_w = (self.map.width  if self.map else 32) - 1
            map_h = (self.map.height if self.map else 16) - 1
            ax = max(0, min(map_w, 2 * mx - x))
            ay = max(0, min(map_h, 2 * my - y))
            print(f"[BOT] Dizzy active — sending anti-target ({ax},{ay}) to reach ({x},{y})",
                  flush=True)
            x, y = ax, ay
        url = f"{self.server_url}/player/move/gameId/{self.game_id}"
        return self._put(url, {"playerId": self.player_id, "newPosition": {"X": x, "Y": y}})

    def move_toward(self, goal, within=None, avoid_spikes=False):
        """Move toward goal as far as this turn allows. If `within` (a pos->bool
        predicate) is given, only step to tiles satisfying it (e.g. the safe zone).
        Uses the route-aware path helper to spend the whole turn budget instead of
        taking only a single step.
        If `avoid_spikes` is True, spike tiles are treated as impassable.
        Returns the server response, or None only if no legal (in-region) move exists."""
        if self.me is None:
            return None
        g = _pos(goal)
        dest = find_path(self.me, goal, avoid_spikes=avoid_spikes)
        if dest is not None and (within is None or within(dest)):
            return self.move(dest)
        # Log why the primary path didn't work before falling back.
        if dest is not None:
            print(f"[BOT] move_toward: find_path→{dest} blocked by within-filter", flush=True)
        else:
            print(f"[BOT] move_toward: find_path→None (no path to {g})", flush=True)
        all_opts = reachable(self.me, 1, avoid_spikes=avoid_spikes)
        options = {p for p in all_opts if within(p)} if within is not None else set(all_opts)
        if not options:
            print(f"[BOT] move_toward: 0/{len(all_opts)} reachable tiles pass filter — STUCK",
                  flush=True)
            return None
        return self.move(min(options, key=lambda p: abs(p[0] - g[0]) + abs(p[1] - g[1])))

    def wander(self, within=None, avoid_spikes=False):
        """Make any legal move (preferring tiles satisfying `within`) so the turn
        always advances — the server has no 'pass'. None only if completely boxed in.
        If `avoid_spikes` is True, spike tiles are excluded from candidates (but if
        spikes are the only option, they are used as a last resort)."""
        if self.me is None:
            return None
        opts = reachable(self.me, 1, avoid_spikes=avoid_spikes)
        pool = {p for p in opts if within(p)} if within is not None else set(opts)
        pool = pool or set(opts)  # if within filtered everything, use any reachable tile
        if not pool and avoid_spikes:
            # Emergency: spike avoidance left us stuck — allow spikes as last resort
            opts = reachable(self.me, 1, avoid_spikes=False)
            pool = {p for p in opts if within(p)} if within is not None else set(opts)
            pool = pool or set(opts)
        if not pool:
            print(f"[BOT] wander: completely boxed in — 0 reachable tiles", flush=True)
            return None
        return self.move(min(pool))

    def can_attack(self, target):
        """True if target is on the same row/col within attack range. Note: the
        server's directed attack skips the +X (right) direction, so an enemy
        directly to the right is treated as not attackable."""
        if self.me is None or target is None:
            return False
        # Attacking while Fatigued throws server-side and leaks the turn lock
        # (every later action then 409s forever) — never do it.
        if self.me.has_status("Fatigue"):
            return False
        mx, my = self.me.position
        try:
            tx, ty = _pos(target)
        except (TypeError, ValueError):
            return False
        if None in (mx, my, tx, ty):
            return False
        if mx != tx and my != ty:
            return False
        dist = abs(mx - tx) + abs(my - ty)
        if dist == 0 or dist > (self.me.attack_range or 1):
            return False
        if my == ty and tx > mx:  # server quirk: right-direction attack is skipped
            return False
        return True

    def attack(self, target):
        tid = target.id if isinstance(target, Entity) else target
        url = f"{self.server_url}/player/{self.player_id}/attack/{tid}/gameId/{self.game_id}"
        return self._put(url)

    def use(self, item):
        iid = item.id if isinstance(item, Item) else item
        url = f"{self.server_url}/player/{self.player_id}/use-item/{iid}/gameId/{self.game_id}"
        return self._put(url)

    def use_healing(self):
        item = self.healing_item()
        return self.use(item) if item else None

    def find_item(self, item_type):
        name = ItemType.NAME.get(item_type)
        if self.me is None:
            return None
        for it in self.me.inventory:
            if _is_item_type(it, item_type, name):
                return it
        return None

    def has_item(self, item_type):
        return self.find_item(item_type) is not None

    def healing_item(self):
        return self.find_item(ItemType.HEALING)

    def pickup(self, target):
        """Pick up an item. target is a position or a full FieldInfo tile."""
        tile = target if isinstance(target, dict) else (self.map.tile_at(target) if self.map else None)
        if not tile:
            return None
        url = f"{self.server_url}/player/pickup/{self.player_id}/gameId/{self.game_id}"
        return self._put(url, tile)

    def pickup_card(self, target):
        tile = target if isinstance(target, dict) else (self.map.tile_at(target) if self.map else None)
        if not tile:
            return None
        url = f"{self.server_url}/map/pickup/{self.player_id}/gameId/{self.game_id}"
        return self._put(url, tile)

    def summon(self, card, pos):
        cid = _get(card, 'Id', 'id') if isinstance(card, dict) else card
        x, y = _pos(pos)
        url = f"{self.server_url}/map/{self.player_id}/summon/{cid}/gameId/{self.game_id}"
        return self._put(url, {"X": x, "Y": y})
