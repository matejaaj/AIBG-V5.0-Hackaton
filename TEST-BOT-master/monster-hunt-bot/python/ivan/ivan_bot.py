"""Monster Hunt Bot — Ivan strategy (priority-based heuristic AI).

Usage:
    python ivan_bot.py <server_url> <game_id> <bot_name> [--debug] [--test]

Strategy summary:
  - Tracks enemy position, HP, inventory and status every turn.
  - Prioritises CC scrolls (Freeze > Confusion) over Healing when
    picking up items, UNLESS HP is already low.
  - Routes through slow-obstacle (snow) tiles; only routes through
    ice-spike tiles when a healing potion is carried (allow_spikes flag).
  - Zone-trap: map shrinks every 15 turns (columns X <= 2 and X >= 29
    collapse first).  When the enemy is in the danger strip with <=2
    turns to the next shrink we freeze/confuse them so they can't escape.
  - Freeze + attack combo: use Freeze Scroll when close, then close in
    and attack the locked enemy.  Optionally follow with Ice Cube summon.
  - Ice Cube + freeze combo: freeze first, summon Ice Cube adjacent to
    the frozen enemy so the monster gets free hits during MonsterTurn.
  - Pickup contest: if the enemy is racing us to a CC scroll or monster
    card we burn a CC scroll to delay them, then sprint to the pickup.
"""
from __future__ import annotations

import json
import logging
import os
import random
import sys
import time
from dataclasses import dataclass
from typing import Optional, Union

import requests

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

POLL_INTERVAL    = 0.2
HTTP_TIMEOUT     = 5.0
MAX_TURNS        = 100
MAP_W            = 32
MAP_H            = 16
CENTER           = (MAP_W // 2, MAP_H // 2)

LOW_HP_RATIO     = 0.30   # critical — retreat / heal in emergency
HEAL_USE_RATIO   = 0.50   # use or chase heal once HP drops here
HEAL_HP_RATIO    = 0.55   # healing pickup priority threshold
LATE_GAME_TURN   = 70
ANTI_STUCK_WINDOW = 4

# Distance threshold: if enemy is farther than this, hunt neutral monsters instead.
# Killing monsters gives +4 HP / +2 ATK (Ice Cube) or +10 HP (Ice Warrior).
HUNT_MONSTER_DIST = 8

# Center-zone offense: when the bot is inside this rectangle it is safe from
# zone collapse and should press the attack instead of farming / picking up.
CENTER_ZONE_X_LO    = 8    # X range of the "safe center" rectangle
CENTER_ZONE_X_HI    = 24
CENTER_ZONE_Y_LO    = 3    # Y range
CENTER_ZONE_Y_HI    = 13
OFFENSE_CHASE_RANGE = 12   # chase enemy when within this many tiles

# Map-shrink mechanics: every SHRINK_INTERVAL turns the outer X columns vanish.
# Danger zone: X=0..2 (left) and X=29..31 (right) collapse first.
SHRINK_INTERVAL  = 15
DANGER_X_LO      = 2   # columns 0-2 vanish (left side)
DANGER_X_HI      = 29  # columns 29-31 vanish (right side)

# Safe-zone lock: once the bot enters X in [SAFE_X_LO, SAFE_X_HI] it never
# returns to the outer strips.  Outer columns become permanently unwalkable.
SAFE_X_LO        = 5
SAFE_X_HI        = 26

# Field types (mirror Models/Field.cs)
F_BASE           = 0
F_NORMAL         = 1
F_OBSTACLE_SLOW  = 2   # snow: safe to walk through, costs +1 movement
F_OBSTACLE       = 3   # ice spikes: 10 DMG; only use when healing carried
F_POWERUP        = 4
F_WALL           = 5
F_EMPTY          = 6

# Snow (F_OBSTACLE_SLOW) is now in WALKABLE — user wants to prefer snow
# over spike shortcuts.  Spike tiles are added dynamically via allow_spikes.
WALKABLE    = {F_BASE, F_NORMAL, F_POWERUP, F_OBSTACLE_SLOW}
DEAD_TERRAIN = {F_WALL, F_EMPTY}

# Status names
S_FROZEN  = "Frozen"
S_FATIGUE = "Fatigue"
S_DIZZY   = "Dizzy"

NEIGHBORS_4 = ((1, 0), (-1, 0), (0, 1), (0, -1))

# ---------------------------------------------------------------------------
# Action dataclasses
# ---------------------------------------------------------------------------

@dataclass
class Move:
    x: int
    y: int

@dataclass
class Attack:
    target_id: int

@dataclass
class UseItem:
    item_id: int

@dataclass
class Pickup:
    x: int
    y: int
    field: dict = None   # full FieldInfo from game state (sent in request body)

@dataclass
class Summon:
    card_id: int
    x: int
    y: int

Action = Union[Move, Attack, UseItem, Pickup, Summon]


def action_to_dict(a: Action) -> dict:
    if isinstance(a, Move):    return {"type": "Move",    "x": a.x, "y": a.y}
    if isinstance(a, Attack):  return {"type": "Attack",  "target": a.target_id}
    if isinstance(a, UseItem): return {"type": "UseItem", "item": a.item_id}
    if isinstance(a, Pickup):  return {"type": "Pickup",  "x": a.x, "y": a.y}
    if isinstance(a, Summon):  return {"type": "Summon",  "card": a.card_id, "x": a.x, "y": a.y}
    return {"type": "Unknown"}


# ---------------------------------------------------------------------------
# HTTP engine
# ---------------------------------------------------------------------------

class Engine:
    def __init__(self, server_url: str, game_id: str, log: logging.Logger):
        self.url = server_url.rstrip("/")
        self.gid = game_id
        self.log = log

    def _req(self, method: str, path: str, body: Optional[dict] = None) -> Optional[dict]:
        url      = f"{self.url}{path}"
        attempts = (1,) if method != "GET" else (1, 2)
        for attempt in attempts:
            try:
                r = requests.request(method, url, json=body, timeout=HTTP_TIMEOUT)
                if 200 <= r.status_code < 300:
                    try:
                        return r.json()
                    except ValueError:
                        return {"_raw": r.text}
                self.log.warning("HTTP %s %s -> %s  %s", method, path, r.status_code, r.text[:200])
                if 400 <= r.status_code < 600:
                    return None
            except requests.RequestException as e:
                self.log.warning("HTTP %s %s attempt %s failed: %s", method, path, attempt, e)
            time.sleep(0.1)
        return None

    def get_state(self) -> Optional[dict]:
        return self._req("GET", f"/game/state/{self.gid}")

    def execute(self, player_id: int, a: Action) -> Optional[dict]:
        gid = self.gid
        if isinstance(a, Move):
            return self._req("PUT", f"/player/move/gameId/{gid}",
                             {"playerId": player_id, "newPosition": {"X": a.x, "Y": a.y}})
        if isinstance(a, Attack):
            return self._req("PUT", f"/player/{player_id}/attack/{a.target_id}/gameId/{gid}")
        if isinstance(a, UseItem):
            return self._req("PUT", f"/player/{player_id}/use-item/{a.item_id}/gameId/{gid}")
        if isinstance(a, Pickup):
            f    = a.field or {}
            item = _g(f, "Item", "item")
            card = _g(f, "MonsterCard", "monsterCard")
            # Build body — server checks fieldInfo.Item / fieldInfo.MonsterCard != null
            body: dict = {"Position": {"X": a.x, "Y": a.y}}
            if item and isinstance(item, dict) and "$ref" not in item and "$Ref" not in item:
                body["Item"] = item
            if card and isinstance(card, dict) and "$ref" not in card and "$Ref" not in card:
                body["MonsterCard"] = card
            # Items → PUT /player/pickup/…   Monster cards → PUT /map/pickup/…
            if card and isinstance(card, dict) and "$ref" not in card and "$Ref" not in card:
                return self._req("PUT", f"/map/pickup/{player_id}/gameId/{gid}", body)
            else:
                return self._req("PUT", f"/player/pickup/{player_id}/gameId/{gid}", body)
        if isinstance(a, Summon):
            return self._req("PUT", f"/map/{player_id}/summon/{a.card_id}/gameId/{gid}",
                             {"X": a.x, "Y": a.y})
        return None


# ---------------------------------------------------------------------------
# State helpers
# ---------------------------------------------------------------------------

def _g(d: Optional[dict], *keys: str, default=None):
    """Return the first present key from a dict (supports case variants)."""
    if not d:
        return default
    for k in keys:
        if k in d:
            return d[k]
    return default


def _pos(p: Optional[dict]) -> Optional[tuple]:
    if not p:
        return None
    x = _g(p, "X", "x")
    y = _g(p, "Y", "y")
    if x is None or y is None:
        return None
    return (int(x), int(y))


# ---------------------------------------------------------------------------
# Item classification helpers
# ---------------------------------------------------------------------------

def _item_type_label(item: Optional[dict]) -> str:
    """Classify an item dict as 'healing', 'freeze', 'confusion', or 'other'."""
    if not item:
        return "other"
    itype = str(_g(item, "ItemType", "itemType", default="")).lower()
    name  = str(_g(item, "Name",     "name",     default="")).lower()
    eff   = str(_g(item, "Effect",   "effect",   default="")).lower()
    if "heal"   in itype or "heal"   in name or "heal"   in eff:
        return "healing"
    if "freeze" in name  or "freeze" in eff  or "frozen" in name or "frozen" in eff:
        return "freeze"
    if "confus" in name  or "confus" in eff  or "dizzy"  in name or "dizzy"  in eff:
        return "confusion"
    return "other"


def _pickup_priority_score(field_entry: dict, hp_ratio: float) -> int:
    """Lower = higher priority.
    0  freeze scroll
    1  confusion scroll
    2  monster card
    3  healing (when HP < HEAL_HP_RATIO)
    6  healing (HP fine)
    7  unknown
    """
    item = _g(field_entry, "Item", "item")
    card = _g(field_entry, "MonsterCard", "monsterCard")
    if item:
        t = _item_type_label(item)
        if t == "freeze":    return 0
        if t == "confusion": return 0  # same urgency as freeze — both are CC
        if t == "healing":   return 3 if hp_ratio < HEAL_HP_RATIO else 6
    if card:
        return 2
    return 7


# ---------------------------------------------------------------------------
# Map-shrink helpers
# ---------------------------------------------------------------------------

def _turns_to_next_shrink(turn_count: int) -> int:
    rem = turn_count % SHRINK_INTERVAL
    return SHRINK_INTERVAL - rem if rem != 0 else SHRINK_INTERVAL


def _is_in_danger_zone(x: int) -> bool:
    return x <= DANGER_X_LO or x >= DANGER_X_HI


# ---------------------------------------------------------------------------
# GameView
# ---------------------------------------------------------------------------

class GameView:
    """Parsed snapshot of one game tick.

    Extra mutable flags set by the bot after construction:
        allow_spikes     – treat F_OBSTACLE as walkable (set when carrying heal)
        rejected_targets – move targets the engine refused this turn
    """

    def __init__(self, state: dict, my_id: int, turn_count: int, tie_seed: int = 0):
        self.raw        = state
        self.my_id      = my_id
        self.turn_count = turn_count
        self.tie_seed   = tie_seed

        # Routing flags (set by bot after construction)
        self.allow_spikes: bool = False
        self.safe_zone_locked: bool = False   # True once bot reached X in [SAFE_X_LO, SAFE_X_HI]
        self.rejected_targets: set = set()
        self.rejected_pickups: set = set()   # pickup positions that failed this turn
        self.rejected_summons: set = set()   # (card_id, x, y) summons that failed this turn
        self.last_was_attack: bool = False    # True when previous action was Attack

        players_dict = _g(state, "Players", "players", default={}) or {}
        players = list(players_dict.values()) if isinstance(players_dict, dict) else list(players_dict)
        self.me:    Optional[dict] = None
        self.enemy: Optional[dict] = None
        for p in players:
            if not p:
                continue
            pid = _g(p, "Id", "id")
            if pid == my_id:
                self.me = p
            else:
                self.enemy = p

        self.map_obj = _g(state, "Map", "map", default={}) or {}
        self.map_w   = int(_g(self.map_obj, "X", "x", default=MAP_W))
        self.map_h   = int(_g(self.map_obj, "Y", "y", default=MAP_H))
        self.grid    = _g(self.map_obj, "Grid", "grid", default=[]) or []

        self._field_by_xy: dict = {}
        for idx, f in enumerate(self.grid):
            if not isinstance(f, dict):
                continue
            p = _pos(_g(f, "Position", "position"))
            if p is None:
                x = idx // self.map_h
                y = idx % self.map_h
                p = (x, y)
            self._field_by_xy[p] = f

        self.monsters:    list = []
        self.my_summons: list = []   # live entities summoned by this player
        self.pickups:    list = []
        for (x, y), f in self._field_by_xy.items():
            ent = _g(f, "Entity", "entity")
            if ent and isinstance(ent, dict):
                if "$ref" not in ent and "$Ref" not in ent:
                    eid        = _g(ent, "Id", "id")
                    summon_by  = _g(ent, "SummonedByPlayerId", "summonedByPlayerId")
                    if summon_by == my_id:
                        self.my_summons.append(ent)         # track own live summons
                    elif (eid is not None
                            and eid != my_id
                            and (self.enemy is None or eid != _g(self.enemy, "Id", "id"))):
                        self.monsters.append(ent)
            if _g(f, "Item", "item") or _g(f, "MonsterCard", "monsterCard"):
                self.pickups.append(((x, y), f))

        # Shortcuts
        self.my_pos     = _pos(_g(self.me,    "Position",        "position"))        if self.me    else None
        self.enemy_pos  = _pos(_g(self.enemy, "Position",        "position"))        if self.enemy else None
        self.my_hp      = int(_g(self.me,    "Health",          "health",          default=0))   if self.me else 0
        self.my_max_hp  = int(_g(self.me,    "MaxHealth",       "maxHealth",       default=100)) if self.me else 100
        self.enemy_hp   = int(_g(self.enemy, "Health",          "health",          default=0))   if self.enemy else 0
        self.my_atk     = int(_g(self.me,    "AttackPower",     "attackPower",     default=10))  if self.me else 10
        self.my_range   = int(_g(self.me,    "AttackRange",     "attackRange",     default=1))   if self.me else 1
        self.my_move    = int(_g(self.me,    "MaxMoveDistance", "maxMoveDistance", default=4))   if self.me else 4
        self.my_inventory:    list = (_g(self.me,    "Inventory",      "inventory",      default=[]) or []) if self.me    else []
        self.my_cards:        list = (_g(self.me,    "Cards",          "cards",          default=[]) or []) if self.me    else []
        self.my_status:       dict = (_g(self.me,    "ActiveStatuses", "activeStatuses", default={}) or {}) if self.me    else {}
        # Enemy inventory / status — tracked for strategic decisions
        self.enemy_inventory: list = (_g(self.enemy, "Inventory",      "inventory",      default=[]) or []) if self.enemy else []
        self.enemy_status:    dict = (_g(self.enemy, "ActiveStatuses", "activeStatuses", default={}) or {}) if self.enemy else {}

        # Grid entities stored as JSON $ref back-references are skipped by the
        # entity scan above.  Patch my_summons by also scanning card monster data.
        _seen_ids = {int(_g(s, "Id", "id") or 0) for s in self.my_summons}
        for _card in self.my_cards:
            _mon = _g(_card, "Monster", "monster")
            if not _mon:
                continue
            _mid = int(_g(_mon, "Id", "id") or 0)
            _mby = _g(_mon, "SummonedByPlayerId", "summonedByPlayerId")
            if _mid != 0 and _mby == my_id and _mid not in _seen_ids:
                self.my_summons.append(_mon)
                _seen_ids.add(_mid)

    # -----------------------------------------------------------------------
    def field(self, x: int, y: int) -> Optional[dict]:
        return self._field_by_xy.get((x, y))

    def field_type(self, x: int, y: int) -> int:
        f  = self.field(x, y)
        if not f:
            return F_EMPTY
        ft = _g(f, "FieldType", "fieldType", default=F_EMPTY)
        return int(ft) if ft is not None else F_EMPTY

    def in_bounds(self, x: int, y: int) -> bool:
        return 0 <= x < self.map_w and 0 <= y < self.map_h

    def is_walkable(self, x: int, y: int, ignore_entities: bool = False) -> bool:
        """True if (x,y) can be entered.
        Ice-spike tiles are allowed only when self.allow_spikes is True
        (set by the bot when a healing potion is in inventory).
        Once safe_zone_locked is True, columns outside [SAFE_X_LO, SAFE_X_HI]
        are treated as impassable.
        """
        if not self.in_bounds(x, y):
            return False
        if self.safe_zone_locked and (x < SAFE_X_LO or x > SAFE_X_HI):
            return False
        ft = self.field_type(x, y)
        walkable = WALKABLE | ({F_OBSTACLE} if self.allow_spikes else set())
        if ft not in walkable:
            return False
        if not ignore_entities:
            f = self.field(x, y)
            if f and _g(f, "Entity", "entity"):
                return False
        return True

    def is_summonable(self, x: int, y: int) -> bool:
        """True if a monster can be summoned at (x,y).
        Rules: in bounds, no entity present, not a wall (F_WALL=5) or lake (F_EMPTY=6).
        Spike tiles (F_OBSTACLE=3) are allowed — unlike walking, summoning doesn't
        require the tile to be passable by the player.  Safe-zone restriction does
        NOT apply to summon placement.
        """
        if not self.in_bounds(x, y):
            return False
        ft = self.field_type(x, y)
        if ft in DEAD_TERRAIN:  # F_WALL=5, F_EMPTY=6
            return False
        f = self.field(x, y)
        if f and _g(f, "Entity", "entity"):
            return False
        return True

    def has_status(self, name: str) -> bool:
        return any(str(k).lower() == name.lower() for k in self.my_status.keys())


# ---------------------------------------------------------------------------
# Item / card finders
# ---------------------------------------------------------------------------

def _find_item_by_type(view: GameView, label: str) -> Optional[dict]:
    for it in view.my_inventory:
        if _item_type_label(it) == label:
            return it
    return None

def _find_freeze_scroll(view: GameView)    -> Optional[dict]: return _find_item_by_type(view, "freeze")
def _find_confusion_scroll(view: GameView) -> Optional[dict]: return _find_item_by_type(view, "confusion")
def _find_healing_item(view: GameView)     -> Optional[dict]: return _find_item_by_type(view, "healing")

def _find_cc_scroll(view: GameView) -> Optional[dict]:
    """Return freeze scroll first, confusion scroll as fallback."""
    return _find_freeze_scroll(view) or _find_confusion_scroll(view)

def _find_ice_cube_card(view: GameView) -> Optional[dict]:
    """Return first ready (non-cooldown) Ice-Cube monster card with no live summon."""
    for c in view.my_cards:
        if bool(_g(c, "OnCooldown", "onCooldown", default=False)):
            continue
        mon  = _g(c, "Monster", "monster") or {}
        # Check card's own Name first, then fall back to monster template name
        card_name = str(_g(c, "Name", "name", default="")).lower()
        mon_name  = str(_g(mon, "Name", "name", default="")).lower()
        if "cube" in card_name or "ice" in card_name or "cube" in mon_name or "ice" in mon_name:
            return c
    return None

def _find_any_usable_card(view: GameView) -> Optional[dict]:
    """Return the highest-attack ready card with no live summon."""
    best = None
    best_atk = -1
    for c in view.my_cards:
        if bool(_g(c, "OnCooldown", "onCooldown", default=False)):
            continue
        mon = _g(c, "Monster", "monster") or {}
        atk = int(_g(mon, "AttackPower", "attackPower", default=0) or 0)
        if atk > best_atk:
            best_atk = atk
            best = c
    return best


# ---------------------------------------------------------------------------
# Enemy status helpers
# ---------------------------------------------------------------------------

def _enemy_is_frozen(view: GameView) -> bool:
    return any("frozen" in str(k).lower() for k in view.enemy_status.keys())

def _enemy_is_dizzy(view: GameView) -> bool:
    return any("dizzy" in str(k).lower() for k in view.enemy_status.keys())

def _enemy_is_cc(view: GameView) -> bool:
    return _enemy_is_frozen(view) or _enemy_is_dizzy(view)


# ---------------------------------------------------------------------------
# Geometry / pathfinding
# ---------------------------------------------------------------------------

def manhattan(a, b) -> int:
    return abs(a[0] - b[0]) + abs(a[1] - b[1])


def attack_cells(attacker: dict) -> set:
    pos = _pos(_g(attacker, "Position", "position"))
    if not pos:
        return set()
    xp  = _g(attacker, "xPattern", default=None) or [1, -1, 0, 0]
    yp  = _g(attacker, "yPattern", default=None) or [0, 0, 1, -1]
    rng = max(1, int(_g(attacker, "AttackRange", "attackRange", default=1) or 1))
    cells = set()
    for dx, dy in zip(xp, yp):
        for r in range(1, rng + 1):
            cells.add((pos[0] + dx * r, pos[1] + dy * r))
    return cells


def reachable_cells(view: GameView, start, max_dist=None) -> dict:
    seen  = {start: 0}
    queue = [start]
    while queue:
        cur = queue.pop(0)
        d   = seen[cur]
        if max_dist is not None and d >= max_dist:
            continue
        for dx, dy in NEIGHBORS_4:
            nb = (cur[0] + dx, cur[1] + dy)
            if nb in seen or not view.is_walkable(*nb):
                continue
            seen[nb] = d + 1
            queue.append(nb)
    return seen


def bfs_from_goal(view: GameView, goal) -> dict:
    """BFS distances from goal to every walkable cell (goal may be occupied)."""
    seen  = {goal: 0}
    queue = [goal]
    while queue:
        cur = queue.pop(0)
        d   = seen[cur]
        for dx, dy in NEIGHBORS_4:
            nb = (cur[0] + dx, cur[1] + dy)
            if nb in seen or not view.is_walkable(*nb):
                continue
            seen[nb] = d + 1
            queue.append(nb)
    return seen


def single_axis_moves(view: GameView, start, max_dist: int) -> list:
    """All legal straight-line move targets (engine requires one axis moves).
    F_OBSTACLE_SLOW (snow) tiles cost 2 movement points; all other walkable
    tiles cost 1.  Movement stops when the budget would go negative.
    Additionally, standing ON a snow tile at the start reduces the budget by 1.
    Server also stops movement at tiles occupied by an Item or MonsterCard.
    """
    # Already standing on snow costs 1 extra movement point this turn
    start_penalty = 1 if view.field_type(*start) == F_OBSTACLE_SLOW else 0
    out = []
    for dx, dy in NEIGHBORS_4:
        budget = max_dist - start_penalty
        for k in range(1, max_dist + 1):
            cx, cy = start[0] + dx * k, start[1] + dy * k
            if not view.is_walkable(cx, cy):
                break
            # Spike tiles (F_OBSTACLE) are impassable as destinations on the
            # server regardless of allow_spikes mode — break without adding.
            if view.field_type(cx, cy) == F_OBSTACLE:
                break
            # Server stops movement at tiles with items or monster cards
            f = view.field(cx, cy)
            if f and (_g(f, "Item", "item") or _g(f, "MonsterCard", "monsterCard")):
                break
            cost = 2 if view.field_type(cx, cy) == F_OBSTACLE_SLOW else 1
            budget -= cost
            if budget < 0:
                break
            if (cx, cy) not in view.rejected_targets:
                out.append((cx, cy))
    return out


def step_toward(view: GameView, start, goal, max_dist: int) -> Optional[tuple]:
    """Pick the best legal single-axis move toward goal.

    If no path exists on normal terrain and we carry healing, retries with
    ice-spike tiles enabled (allow_spikes fallback).
    """
    dist_from_goal = bfs_from_goal(view, goal)
    big  = view.map_w * view.map_h + 1
    seed = view.tie_seed

    candidates = single_axis_moves(view, start, max_dist)
    if not candidates:
        if not view.allow_spikes and _find_healing_item(view):
            view.allow_spikes = True
            result = step_toward(view, start, goal, max_dist)
            view.allow_spikes = False
            return result
        return None

    def score(c):
        d = dist_from_goal.get(c, big + manhattan(c, goal))
        return (d, hash((c, seed)) & 0xFFFF)

    return min(candidates, key=score)


# ---------------------------------------------------------------------------
# Core predicates
# ---------------------------------------------------------------------------

def _enemy_in_range(view: GameView) -> bool:
    if not view.me or not view.enemy or not view.enemy_pos:
        return False
    return view.enemy_pos in attack_cells(view.me)


def _monster_in_range(view: GameView) -> Optional[dict]:
    if not view.me:
        return None
    cells   = attack_cells(view.me)
    best    = None
    best_hp = 10**9
    for m in view.monsters:
        mp = _pos(_g(m, "Position", "position"))
        if mp and mp in cells:
            hp = int(_g(m, "Health", "health", default=10**9))
            if hp < best_hp:
                best_hp = hp
                best = m
    return best


# ---------------------------------------------------------------------------
# Decision functions
# ---------------------------------------------------------------------------

def decide_lethal(view: GameView) -> Optional[Action]:
    """Finish the enemy in one hit."""
    if view.last_was_attack:
        return None
    if _enemy_in_range(view) and view.my_atk >= view.enemy_hp > 0:
        return Attack(int(_g(view.enemy, "Id", "id")))
    return None


def decide_emergency(view: GameView) -> Optional[Action]:
    """Heal or retreat when HP is critically low."""
    if not view.me or not view.my_pos:
        return None
    ratio = view.my_hp / max(1, view.my_max_hp)
    if ratio >= LOW_HP_RATIO:
        return None
    heal = _find_healing_item(view)
    if heal:
        return UseItem(int(_g(heal, "Id", "id")))
    target = CENTER
    if view.enemy_pos:
        tx = view.my_pos[0] + (view.my_pos[0] - view.enemy_pos[0])
        ty = view.my_pos[1] + (view.my_pos[1] - view.enemy_pos[1])
        target = (max(2, min(view.map_w - 3, tx)), max(2, min(view.map_h - 3, ty)))
    nxt = step_toward(view, view.my_pos, target, view.my_move)
    if nxt:
        return Move(nxt[0], nxt[1])
    return None


def decide_on_tile_pickup(view: GameView) -> Optional[Action]:
    """Handle being ON an item tile.

    The server's PickupItem/PickupCard only accepts calls when the item is
    ADJACENT (dist=1) to the player, NOT when the player is standing on it.
    When we are on the tile we therefore move to a free neighbour so that
    next turn the item is adjacent and we can pick it up.
    """
    if not view.my_pos or not view.pickups:
        return None
    for (x, y), f in view.pickups:
        if (x, y) == view.my_pos:
            # Step off to any free neighbour — item will be adjacent next turn
            for dx, dy in NEIGHBORS_4:
                nx, ny = x + dx, y + dy
                if view.is_walkable(nx, ny):
                    return Move(nx, ny)
    return None


def decide_adjacent_cc_pickup(view: GameView) -> Optional[Action]:
    """Pick up a Freeze/Confusion scroll or monster card when adjacent.

    Runs before combat so that high-value items are never skipped because
    an attack opportunity appeared on the same turn.
    """
    if not view.my_pos or not view.pickups:
        return None
    hp_ratio = view.my_hp / max(1, view.my_max_hp)
    for (x, y), f in view.pickups:
        if _pickup_priority_score(f, hp_ratio) > 2:   # only CC scrolls (0,1) and cards (2)
            continue
        if manhattan(view.my_pos, (x, y)) == 1 and (x, y) not in view.rejected_pickups:
                return Pickup(x, y, f)


def decide_zone_trap(view: GameView) -> Optional[Action]:
    """Freeze/confuse the enemy when they are in the collapsing columns
    (X <= DANGER_X_LO or X >= DANGER_X_HI) and the shrink is <=2 turns away.

    Freeze preferred (enemy can't walk out); confusion as fallback.
    Skip if the enemy is already CC'd — the scroll would be wasted.
    """
    if not view.enemy_pos:
        return None
    ex, _ = view.enemy_pos
    if not _is_in_danger_zone(ex):
        return None
    if _turns_to_next_shrink(view.turn_count) > 2:
        return None
    if _enemy_is_cc(view):
        return None
    scroll = _find_cc_scroll(view)
    if scroll:
        return UseItem(int(_g(scroll, "Id", "id")))
    return None


def decide_escape_spike(view: GameView) -> Optional[Action]:
    """If standing on a spike tile (F_OBSTACLE), immediately step off onto safe ground."""
    if not view.my_pos:
        return None
    if view.field_type(*view.my_pos) != F_OBSTACLE:
        return None
    x, y = view.my_pos
    for dx, dy in NEIGHBORS_4:
        nx, ny = x + dx, y + dy
        if view.in_bounds(nx, ny) and view.field_type(nx, ny) in WALKABLE and (nx, ny) not in view.rejected_targets:
            return Move(nx, ny)
    return None


def decide_edge_safety(view: GameView) -> Optional[Action]:
    """Retreat inward if we are standing near collapsed/collapsing terrain."""
    if view.has_status(S_FROZEN) or not view.my_pos:
        return None
    x, y = view.my_pos
    # Count dead-or-imminently-collapsing neighbours within radius 2
    dead = 0
    total = 0
    ttl = _turns_to_next_shrink(view.turn_count)
    for dx in range(-2, 3):
        for dy in range(-2, 3):
            if dx == 0 and dy == 0:
                continue
            nx, ny = x + dx, y + dy
            total += 1
            ft = view.field_type(nx, ny) if view.in_bounds(nx, ny) else F_EMPTY
            if (not view.in_bounds(nx, ny)
                    or ft in DEAD_TERRAIN
                    or (_is_in_danger_zone(nx) and ttl <= 1)):
                dead += 1
    on_dead = (view.field_type(x, y) in DEAD_TERRAIN
               or (_is_in_danger_zone(x) and ttl <= 1))
    if not on_dead and dead / max(1, total) < 0.45:
        return None
    nxt = step_toward(view, view.my_pos, CENTER, view.my_move)
    if nxt and nxt != view.my_pos:
        return Move(nxt[0], nxt[1])
    return None


def _should_use_cc_now(view: GameView) -> bool:
    """Return True only when it is a good moment to spend a CC scroll.

    We save Freeze / Confusion scrolls for two situations:
      1. Offensive mode: bot is inside the center zone AND the enemy is
         close — we are pressing the attack, best time to lock them down.
      2. Zone pressure: our own tile is in the danger zone and the next
         shrink is ≤ 2 turns away — use it before we lose the position.
    """
    if not view.my_pos:
        return False
    x, y = view.my_pos
    # Offensive mode: in center zone and enemy is nearby
    in_center = (CENTER_ZONE_X_LO <= x <= CENTER_ZONE_X_HI and
                 CENTER_ZONE_Y_LO <= y <= CENTER_ZONE_Y_HI)
    if (in_center and view.enemy_pos and
            manhattan(view.my_pos, view.enemy_pos) <= OFFENSE_CHASE_RANGE):
        return True
    # Zone pressure: our position is about to be consumed by the shrink
    if _is_in_danger_zone(x) and _turns_to_next_shrink(view.turn_count) <= 2:
        return True
    return False


def decide_ice_cube_freeze(view: GameView) -> Optional[Action]:
    """Ice Cube + Freeze combo.

    Step 1 — enemy is free:   freeze them so they can't run.
    Step 2 — enemy is frozen: summon Ice Cube adjacent to them
                               (it will attack during MonsterTurn).
    After the cube is placed we continue attacking via decide_freeze_combo.
    """
    if not view.enemy_pos or not view.my_pos:
        return None
    ice_card = _find_ice_cube_card(view)
    if not ice_card:
        return None
    if manhattan(view.my_pos, view.enemy_pos) > view.my_move + 4:
        return None

    if not _enemy_is_frozen(view):
        freeze = _find_freeze_scroll(view)
        # Only spend the scroll when it's a good moment — save it otherwise.
        if freeze and _should_use_cc_now(view):
            return UseItem(int(_g(freeze, "Id", "id")))
        return None  # no freeze or not the right time — can't set up the combo

    # Enemy is frozen — place the cube on the adjacent-to-US tile closest to
    # the enemy (server only allows summon 1 step from the player, not 1 step
    # from the enemy).
    mx, my = view.my_pos
    best_cell = None
    best_dist = 10**9
    for dx, dy in NEIGHBORS_4:
        sx, sy = mx + dx, my + dy
        if view.is_summonable(sx, sy):
            d = manhattan((sx, sy), view.enemy_pos)
            if d < best_dist:
                best_dist = d
                best_cell = (sx, sy)
    if best_cell:
        return Summon(int(_g(ice_card, "Id", "id")), best_cell[0], best_cell[1])

    # No free adjacent cell — fall back to attacking the frozen enemy
    if _enemy_in_range(view) and not view.last_was_attack:
        return Attack(int(_g(view.enemy, "Id", "id")))
    nxt = step_toward(view, view.my_pos, view.enemy_pos, view.my_move)
    if nxt:
        return Move(nxt[0], nxt[1])
    return None


def decide_freeze_combo(view: GameView) -> Optional[Action]:
    """Freeze-and-attack strategy.

    Phase A (enemy frozen):  close in and swing — enemy cannot flee.
    Phase B (enemy free):    use Freeze Scroll when within striking range,
                             but only when we don't have an Ice Cube card
                             ready (let decide_ice_cube_freeze handle that).
    """
    if not view.enemy_pos or not view.my_pos:
        return None

    if _enemy_is_frozen(view):
        # Enemy can't move — maximise damage (but not two attacks in a row)
        if _enemy_in_range(view) and not view.last_was_attack:
            return Attack(int(_g(view.enemy, "Id", "id")))
        nxt = step_toward(view, view.my_pos, view.enemy_pos, view.my_move)
        if nxt:
            return Move(nxt[0], nxt[1])
        return None

    dist  = manhattan(view.my_pos, view.enemy_pos)
    # Only CC when we can reach + attack in one turn (truly "close").
    # Also verify the scroll is actually in inventory before using it.
    # And only spend it when the timing is right (offensive / zone-pressure).
    close = view.my_move + view.my_range
    if dist <= close and _should_use_cc_now(view):
        # If we also have an ice cube, the richer combo should fire first
        if not _find_ice_cube_card(view):
            # Freeze preferred (enemy locked in place); confusion as fallback
            # (enemy moves randomly — still disrupts their positioning)
            cc = _find_freeze_scroll(view) or _find_confusion_scroll(view)
            if cc:  # inventory check: returns None if not carried
                return UseItem(int(_g(cc, "Id", "id")))
    return None


def decide_low_hp_heal(view: GameView) -> Optional[Action]:
    """Use a carried heal, or move toward the nearest heal pickup, when HP < 50%.

    Fires before combat decisions so the bot actively prioritises survival.
    If no heal exists anywhere on the map the function returns None and the
    normal combat pipeline continues.
    """
    if not view.me or not view.my_pos:
        return None
    if view.my_hp / max(1, view.my_max_hp) >= HEAL_USE_RATIO:
        return None

    # Already carrying a heal — consume it immediately
    heal = _find_healing_item(view)
    if heal:
        return UseItem(int(_g(heal, "Id", "id")))

    # No heal in inventory — find the nearest reachable heal tile on the map
    if not view.pickups:
        return None
    INF    = view.map_w * view.map_h + 1
    my_bfs = reachable_cells(view, view.my_pos)
    best_pos  = None
    best_dist = INF
    for (x, y), f in view.pickups:
        item = _g(f, "Item", "item")
        if not item or _item_type_label(item) != "healing":
            continue
        d = my_bfs.get((x, y), INF)
        if d < best_dist:
            best_dist = d
            best_pos  = (x, y)

    if best_pos is None:
        return None  # no heal on map — let combat pipeline handle it

    if manhattan(view.my_pos, best_pos) == 1 and best_pos not in view.rejected_pickups:
        return Pickup(best_pos[0], best_pos[1], view.field(*best_pos))
    nxt = step_toward(view, view.my_pos, best_pos, view.my_move)
    if nxt:
        return Move(nxt[0], nxt[1])
    return None


def decide_opportunity(view: GameView) -> Optional[Action]:
    """Attack whenever a target is in range — enemy player first, then any monster.

    No HP/ATK conditions: always swing when you can.  Killing neutral monsters
    grants HP and ATK buffs (server: BuffPlayers), so it is never wasteful.
    """
    if view.last_was_attack:
        return None
    # Enemy player — highest priority
    if _enemy_in_range(view) and view.enemy_hp > 0:
        return Attack(int(_g(view.enemy, "Id", "id")))
    # Any neutral monster in range — free buff
    mon = _monster_in_range(view)
    if mon is not None:
        return Attack(int(_g(mon, "Id", "id")))
    return None


def decide_summon_close(view: GameView) -> Optional[Action]:
    """Summon the highest-ATK card when the enemy is within close range.

    Fires when the enemy is within my_move + 2 tiles so the summoned monster
    can reach and attack them on the very next MonsterTurn.  The spawn cell
    must be an adjacent, unoccupied, walkable tile (server rejects occupied
    positions).  Prefers the adjacent cell closest to the enemy.
    """
    if not view.my_cards or not view.my_pos or not view.enemy_pos:
        return None
    if view.my_summons:                  # already have a live summon on the field
        return None
    if manhattan(view.my_pos, view.enemy_pos) > view.my_move + 8:
        return None  # enemy too far — let decide_summon handle it

    card = _find_any_usable_card(view)
    if not card:
        return None
    card_id = int(_g(card, "Id", "id"))
    mx, my  = view.my_pos

    candidates = []
    for dx, dy in NEIGHBORS_4:
        sx, sy = mx + dx, my + dy
        if view.is_summonable(sx, sy):
            candidates.append((manhattan((sx, sy), view.enemy_pos), sx, sy))
    if not candidates:
        return None
    candidates.sort()
    for _, sx, sy in candidates:
        if (card_id, sx, sy) not in view.rejected_summons:
            return Summon(card_id, sx, sy)
    return None


def decide_contest_pickup(view: GameView) -> Optional[Action]:
    """Burn a CC scroll when the enemy is racing us to a CC scroll or monster card.

    Triggers when:
    - The tile holds a CC scroll (score 0-1) or a monster card (score 2).
    - The enemy is AT LEAST as close to the tile as we are (by actual BFS path).
    - The enemy is not already CC'd (scroll would be wasted).

    After using the scroll the bot immediately steps toward the tile.
    """
    if not view.enemy_pos or not view.my_pos or not view.pickups:
        return None
    hp_ratio = view.my_hp / max(1, view.my_max_hp)
    INF = view.map_w * view.map_h + 1

    # BFS from each position so walls/empty tiles are respected
    my_bfs    = reachable_cells(view, view.my_pos)
    enemy_bfs = reachable_cells(view, view.enemy_pos)

    # Only contest pickups we can realistically reach ourselves.
    # Burning a CC scroll from across the map is wasteful — the freeze wears
    # off long before we arrive.  Threshold: our BFS distance must be < half
    # the map area (i.e. the tile is actually reachable in reasonable time).
    MAX_CONTEST_DIST = (view.map_w + view.map_h) * 2

    sorted_pus = sorted(view.pickups,
                        key=lambda p: _pickup_priority_score(p[1], hp_ratio))
    for (px, py), f in sorted_pus:
        if _pickup_priority_score(f, hp_ratio) > 3:
            continue  # only CC scrolls and cards worth contesting

        my_dist    = my_bfs.get((px, py),    INF)
        enemy_dist = enemy_bfs.get((px, py), INF)
        if enemy_dist > my_dist:
            continue  # we are already ahead
        if my_dist > MAX_CONTEST_DIST:
            continue  # too far away — CC scroll won't help us get there in time

        if _enemy_is_cc(view):
            # Enemy already slowed — sprint to the tile
            nxt = step_toward(view, view.my_pos, (px, py), view.my_move)
            if nxt:
                return Move(nxt[0], nxt[1])
            return None

        scroll = _find_cc_scroll(view)
        if scroll:
            return UseItem(int(_g(scroll, "Id", "id")))

    return None


def decide_pickup(view: GameView) -> Optional[Action]:
    """Pick up items with priority: Freeze > Confusion > Cards > Healing (low HP) > rest.

    Endpoint: PUT /map/pickup/{playerId}/gameId/{gameId}
              Body: { "Position": { "X": item_x, "Y": item_y } }

    The server accepts a pickup when the player is ON or ADJACENT to the item
    tile.  We therefore send Pickup(item_pos) in both cases — no need to waste
    a turn moving onto the tile first.

    Sorting and distance checks use actual BFS distance (not Manhattan) so that
    walls and empty tiles are respected when choosing which item to chase.

    1. On the pickup tile or adjacent → Pickup(item_x, item_y) immediately.
    2. Reachable within move budget+1 → Move toward the tile.
    3. Otherwise                      → walk toward best worthwhile pickup.
    """
    if not view.my_pos or not view.pickups:
        return None
    hp_ratio = view.my_hp / max(1, view.my_max_hp)
    INF = view.map_w * view.map_h + 1

    # Full BFS from our position — respects walls and empty tiles
    my_bfs = reachable_cells(view, view.my_pos)

    sorted_pus = sorted(
        view.pickups,
        key=lambda pu: (_pickup_priority_score(pu[1], hp_ratio), my_bfs.get(pu[0], INF))
    )

    # 1) Adjacent (dist==1) → pickup directly (saves one turn).
    #    dist==0 (on-tile) is handled by decide_on_tile_pickup which steps off first.
    for (x, y), f in sorted_pus:
        if manhattan(view.my_pos, (x, y)) == 1 and (x, y) not in view.rejected_pickups:
            return Pickup(x, y, f)

    # 2) Within move range by actual BFS path → step toward; pickup next turn
    for (x, y), f in sorted_pus:
        if _pickup_priority_score(f, hp_ratio) > 5:
            continue
        if my_bfs.get((x, y), INF) <= view.my_move + 1:
            nxt = step_toward(view, view.my_pos, (x, y), view.my_move)
            if nxt:
                return Move(nxt[0], nxt[1])

    # 3) Farther away → walk toward best worthwhile reachable pickup
    for (x, y), f in sorted_pus:
        if _pickup_priority_score(f, hp_ratio) > 5:
            continue
        if my_bfs.get((x, y), INF) < INF:  # only attempt if actually reachable
            nxt = step_toward(view, view.my_pos, (x, y), view.my_move)
            if nxt:
                return Move(nxt[0], nxt[1])

    return None


def decide_summon(view: GameView) -> Optional[Action]:
    """Summon highest-ATK available card whenever a card is ready.

    Fires any time we have a non-cooldown card and no live summon on the field.
    Prefers the adjacent cell closest to the enemy (or to the map centre when
    the enemy position is unknown).
    """
    if not view.my_cards or not view.my_pos:
        return None
    if view.my_summons:                  # already have a live summon on the field
        return None
    card = _find_any_usable_card(view)
    if not card:
        return None
    card_id = int(_g(card, "Id", "id"))
    mx, my  = view.my_pos
    ref_pos = view.enemy_pos if view.enemy_pos else CENTER

    # Only tiles that are exactly 1 step from the player are valid summon spots
    candidates = []
    for dx, dy in NEIGHBORS_4:
        sx, sy = mx + dx, my + dy
        if view.is_summonable(sx, sy):
            d = manhattan((sx, sy), ref_pos)
            candidates.append((d, sx, sy))
    if not candidates:
        return None
    candidates.sort()
    for _, sx, sy in candidates:
        if (card_id, sx, sy) not in view.rejected_summons:
            return Summon(card_id, sx, sy)
    return None


def decide_hunt_monsters(view: GameView) -> Optional[Action]:
    """Chase and kill neutral monsters when the enemy is far away.

    Killing a neutral monster gives permanent buffs (server BuffPlayers):
      Ice Cube / default  → +4 HP, +4 MaxHP, +2 ATK
      Ice Warrior         → +10 HP, +10 MaxHP
      Ice Mage            → +10 ATK

    Fires only when the enemy is more than HUNT_MONSTER_DIST tiles away so
    the bot does not ignore a nearby enemy in favour of farming.
    """
    if not view.my_pos or not view.monsters:
        return None
    if view.enemy_pos and manhattan(view.my_pos, view.enemy_pos) <= HUNT_MONSTER_DIST:
        return None  # enemy is close — keep focus on them

    # Attack a monster already in range (no cooldown check needed for attacks)
    if not view.last_was_attack:
        mon = _monster_in_range(view)
        if mon is not None:
            return Attack(int(_g(mon, "Id", "id")))

    # Chase the nearest reachable monster
    INF = view.map_w * view.map_h + 1
    my_bfs = reachable_cells(view, view.my_pos)
    best_pos  = None
    best_dist = INF
    for m in view.monsters:
        mp = _pos(_g(m, "Position", "position"))
        if mp:
            d = my_bfs.get(mp, INF)
            if d < best_dist:
                best_dist = d
                best_pos  = mp
    if best_pos and best_dist < INF:
        nxt = step_toward(view, view.my_pos, best_pos, view.my_move)
        if nxt:
            return Move(nxt[0], nxt[1])
    return None


def decide_powerup(view: GameView) -> Optional[Action]:
    """Move toward the highest-priority reachable POWERUP tile."""
    if not view.my_pos:
        return None
    hp_ratio = view.my_hp / max(1, view.my_max_hp)
    cells    = reachable_cells(view, view.my_pos, view.my_move)
    best_score = (99, 99, None)
    for cell, dist in cells.items():
        if cell == view.my_pos or view.field_type(*cell) != F_POWERUP:
            continue
        f    = view.field(*cell)
        prio = _pickup_priority_score(f or {}, hp_ratio) if f else 7
        if (prio, dist) < (best_score[0], best_score[1]):
            best_score = (prio, dist, cell)
    if best_score[2] is not None:
        c = best_score[2]
        return Move(c[0], c[1])
    return None


def decide_pressure(view: GameView) -> Optional[Action]:
    """Chase the enemy or push them toward the edge in late game."""
    if view.has_status(S_FROZEN) or not view.my_pos or not view.enemy_pos:
        return None
    target = view.enemy_pos
    if view.turn_count >= LATE_GAME_TURN and view.my_hp > view.enemy_hp:
        ex, ey = view.enemy_pos
        push_x = 0 if ex < view.map_w // 2 else view.map_w - 1
        target = (push_x, ey)
    nxt = step_toward(view, view.my_pos, target, view.my_move)
    if nxt and nxt != view.my_pos:
        return Move(nxt[0], nxt[1])
    moves = single_axis_moves(view, view.my_pos, view.my_move)
    if moves:
        c = max(moves, key=lambda c: manhattan(c, view.my_pos))
        return Move(c[0], c[1])
    return None


def _nearest_walkable_to(view: GameView, cx: int, cy: int) -> tuple:
    """Return the walkable cell nearest to (cx, cy), scanning outward."""
    if view.is_walkable(cx, cy, ignore_entities=True):
        return (cx, cy)
    for r in range(1, max(view.map_w, view.map_h)):
        for dx in range(-r, r + 1):
            for dy in range(-r, r + 1):
                if abs(dx) == r or abs(dy) == r:
                    x, y = cx + dx, cy + dy
                    if view.is_walkable(x, y, ignore_entities=True):
                        return (x, y)
    return (cx, cy)


def decide_center_offense(view: GameView) -> Optional[Action]:
    """When safely positioned in the center zone, chase the enemy directly.

    Fires BEFORE summon/pickup/hunt decisions so the bot uses its safe
    central position to press the attack rather than detouring for items.
    Only active when the enemy is within OFFENSE_CHASE_RANGE tiles — if
    they are far away the normal pickup / hunt pipeline is more efficient.
    """
    if not view.my_pos or not view.enemy_pos:
        return None
    if view.has_status(S_FROZEN):
        return None
    x, y = view.my_pos
    if not (CENTER_ZONE_X_LO <= x <= CENTER_ZONE_X_HI and
            CENTER_ZONE_Y_LO <= y <= CENTER_ZONE_Y_HI):
        return None
    if manhattan(view.my_pos, view.enemy_pos) > OFFENSE_CHASE_RANGE:
        return None
    nxt = step_toward(view, view.my_pos, view.enemy_pos, view.my_move)
    if nxt and nxt != view.my_pos:
        return Move(nxt[0], nxt[1])
    return None


def decide_fallback(view: GameView) -> Optional[Action]:
    """Last resort: move toward centre or any walkable neighbour."""
    if view.has_status(S_FROZEN) or not view.my_pos:
        return None
    goal = _nearest_walkable_to(view, CENTER[0], CENTER[1])
    nxt = step_toward(view, view.my_pos, goal, view.my_move)
    if nxt and nxt != view.my_pos:
        return Move(nxt[0], nxt[1])
    rng = random.Random(hash(view.my_pos) & 0xFFFF)
    opts = list(NEIGHBORS_4)
    rng.shuffle(opts)
    # Prefer truly free neighbours; only fall back to entity-occupied cells
    # as an absolute last resort (avoids infinite loop caused by repeatedly
    # trying to enter a cell blocked by our own summoned monster).
    for dx, dy in opts:
        nx, ny = view.my_pos[0] + dx, view.my_pos[1] + dy
        if view.is_walkable(nx, ny):          # respects entities
            return Move(nx, ny)
    for dx, dy in opts:
        nx, ny = view.my_pos[0] + dx, view.my_pos[1] + dy
        if view.is_walkable(nx, ny, ignore_entities=True):
            return Move(nx, ny)
    return None


# ---------------------------------------------------------------------------
# Priority pipeline
# ---------------------------------------------------------------------------

PRIORITIES = [
    # 1. Instant kill
    ("lethal",               decide_lethal),
    # 2. Self-preservation (critical — <30% HP)
    ("emergency",            decide_emergency),
    # 3. Standing ON an item tile — always grab before anything else
    ("on_tile_pickup",       decide_on_tile_pickup),
    # 3b. Escape spike tile immediately (takes damage every turn)
    ("escape_spike",         decide_escape_spike),
    # 4. Adjacent to a CC scroll / card — grab before combat
    ("adjacent_cc_pickup",   decide_adjacent_cc_pickup),
    # 5. Use/seek heal when HP < 50%  (survival priority)
    ("low_hp_heal",          decide_low_hp_heal),
    # 6. Zone trap (time-sensitive — must run before edge_safety)
    ("zone_trap",            decide_zone_trap),
    # 7. Escape dying terrain ourselves
    ("edge_safety",          decide_edge_safety),
    # 8. Ice Cube + freeze: freeze → summon cube adjacent to frozen enemy
    ("ice_cube_freeze",      decide_ice_cube_freeze),
    # 9. Simple freeze → close in → attack cycle
    ("freeze_combo",         decide_freeze_combo),
    # 10. Attack when already in range (enemy first, then any monster)
    ("opportunity",          decide_opportunity),
    # 11. Center-zone offense: chase enemy before any pickup/hunt detours
    ("center_offense",       decide_center_offense),
    # 12. Summon card when enemy is within range (my_move + 8)
    ("summon_close",         decide_summon_close),
    # 12. Summon whenever a card is ready and no live summon exists
    ("summon",               decide_summon),
    # 13. Block enemy from grabbing CC scroll / card
    ("contest_pickup",       decide_contest_pickup),
    # 14. Pick up remaining items (healing etc.) + approach logic
    ("pickup",               decide_pickup),
    # 15. Hunt neutral monsters for HP/ATK buffs when enemy is far
    ("hunt_monsters",        decide_hunt_monsters),
    # 16. Move toward a POWERUP tile
    ("powerup",              decide_powerup),
    # 17. Chase enemy / late-game edge push
    ("pressure",             decide_pressure),
    # 18. Move toward centre / random step
    ("fallback",             decide_fallback),
]


# ---------------------------------------------------------------------------
# Bot orchestration
# ---------------------------------------------------------------------------

class MonsterHuntBot:
    def __init__(self, server_url: str, game_id: str, bot_name: str, debug: bool = False):
        self.bot_name   = bot_name
        self.game_id    = game_id
        self.debug      = debug
        self.player_id  = None
        self.turn_count = 0
        self._tie_seed  = hash(bot_name) & 0xFFFF
        self.history    = []
        self._rejected_targets = set()
        self._rejected_pickups: set = set()
        self._rejected_summons: set = set()
        self._reached_safe_zone: bool = False   # latches True once inside X [SAFE_X_LO, SAFE_X_HI]
        self._last_was_attack:   bool = False   # tracks back-to-back attack prevention
        self._turn_key  = None
        self.log    = self._setup_logger(bot_name)
        self.engine = Engine(server_url, game_id, self.log)

        logs_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logs")
        os.makedirs(logs_dir, exist_ok=True)
        self.dataset_path = os.path.join(logs_dir, f"{game_id}.txt")

    # -----------------------------------------------------------------------
    def _dataset_log(self, state: dict, label: str, action: Action) -> None:
        record = {
            "ts": time.time(), "game_id": self.game_id,
            "bot_name": self.bot_name, "player_id": self.player_id,
            "turn": self.turn_count,
            "phase": _g(state, "GameState", "gameState"),
            "decision": label, "action": action_to_dict(action), "state": state,
        }
        try:
            with open(self.dataset_path, "a", encoding="utf-8") as fh:
                fh.write(json.dumps(record, separators=(",", ":")) + "\n")
        except OSError as e:
            self.log.warning("dataset log write failed: %s", e)

    @staticmethod
    def _setup_logger(name: str) -> logging.Logger:
        os.makedirs("logs", exist_ok=True)
        logger = logging.getLogger(f"bot.{name}")
        logger.setLevel(logging.INFO)
        if not logger.handlers:
            fh = logging.FileHandler(f"logs/{name}.log", mode="w")
            fh.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
            logger.addHandler(fh)
            sh = logging.StreamHandler()
            sh.setFormatter(logging.Formatter("[%(name)s] %(message)s"))
            logger.addHandler(sh)
        return logger

    def find_my_player_id(self, state: dict) -> bool:
        players = _g(state, "Players", "players", default={}) or {}
        items   = players.values() if isinstance(players, dict) else players
        for p in items:
            if p and _g(p, "Name", "name") == self.bot_name:
                self.player_id = int(_g(p, "Id", "id"))
                return True
        return False

    def is_my_turn(self, state: dict) -> bool:
        if not state or self.player_id is None:
            return False
        players = _g(state, "Players", "players", default={}) or {}
        my = None
        if isinstance(players, dict):
            my = players.get(str(self.player_id)) or players.get(self.player_id)
        if my is None:
            for p in (players.values() if isinstance(players, dict) else players):
                if p and _g(p, "Id", "id") == self.player_id:
                    my = p
                    break
        if not my:
            return False
        first = bool(_g(my, "First", "first", default=False))
        gs    = _g(state, "GameState", "gameState", default="")
        return (first and gs == "Player1Turn") or (not first and gs == "Player2Turn")

    def is_game_over(self, state: dict) -> bool:
        return bool(state) and _g(state, "GameState", "gameState", default="") == "Ending"

    # -----------------------------------------------------------------------

    def _is_stuck(self) -> bool:
        if len(self.history) < ANTI_STUCK_WINDOW:
            return False
        recent    = self.history[-ANTI_STUCK_WINDOW:]
        positions = {h[1] for h in recent}
        actions   = {h[2] for h in recent}
        no_combat = not any(a in ("lethal", "opportunity", "freeze_combo",
                                  "ice_cube_freeze", "zone_trap",
                                  "summon", "summon_close") for a in actions)
        return len(positions) <= 2 and no_combat

    def _unstick(self, view: GameView) -> Optional[Action]:
        if not view.my_pos:
            return None
        candidates = single_axis_moves(view, view.my_pos, view.my_move)
        if not candidates:
            return None
        mx, my = view.my_pos
        if view.enemy_pos:
            ex, ey     = view.enemy_pos
            horiz_axis = abs(ex - mx) >= abs(ey - my)
        else:
            horiz_axis = True
        seed = (hash(self.bot_name) ^ self.turn_count) & 0xFFFF

        def score(c):
            steps = manhattan(c, view.my_pos)
            perp  = abs(c[1] - my) if horiz_axis else abs(c[0] - mx)
            return (-steps, -perp, hash((c, seed)) & 0xFFFF)

        candidates.sort(key=score)
        nx, ny = candidates[0]
        return Move(nx, ny)

    def pick_action(self, view: GameView):
        if self._is_stuck():
            # Pickup is always a valid unsticking action — try it first
            a = decide_pickup(view)
            if a is not None:
                return ("pickup", a)
            a = self._unstick(view)
            if a is not None:
                return ("anti_stuck", a)
        for label, fn in PRIORITIES:
            try:
                a = fn(view)
            except Exception as e:
                self.log.exception("decision %s threw: %s", label, e)
                a = None
            if a is not None:
                return (label, a)
        return ("none", None)

    # -----------------------------------------------------------------------

    def render_ascii(self, view: GameView) -> str:
        rows = []
        for y in range(view.map_h - 1, -1, -1):
            row = []
            for x in range(view.map_w):
                ch = "."
                ft = view.field_type(x, y)
                if ft == F_WALL:           ch = "#"
                elif ft == F_EMPTY:        ch = " "
                elif ft == F_OBSTACLE:     ch = "x"
                elif ft == F_OBSTACLE_SLOW:ch = "~"
                elif ft == F_POWERUP:      ch = "+"
                elif ft == F_BASE:         ch = "B"
                f = view.field(x, y)
                if f and (_g(f, "Item", "item") or _g(f, "MonsterCard", "monsterCard")):
                    ch = "*"
                if view.my_pos == (x, y):
                    ch = "M"
                elif view.enemy_pos == (x, y):
                    ch = "E"
                else:
                    for m in view.monsters:
                        if _pos(_g(m, "Position", "position")) == (x, y):
                            ch = "m"
                            break
                row.append(ch)
            rows.append("".join(row))
        return "\n".join(rows)

    # -----------------------------------------------------------------------

    def take_turn(self, state: dict) -> Optional[dict]:
        self.turn_count += 1
        game_turn = int(_g(state, "TurnCounter", "turnCounter",
                           default=self.turn_count) or self.turn_count)

        turn_key = (game_turn, _g(state, "GameState", "gameState"))
        if turn_key != self._turn_key:
            self._rejected_targets.clear()
            self._rejected_pickups.clear()
            self._rejected_summons.clear()
            self._turn_key = turn_key

        view = GameView(state, self.player_id, game_turn, tie_seed=self._tie_seed)
        view.rejected_targets = self._rejected_targets
        view.rejected_pickups = self._rejected_pickups
        view.rejected_summons = self._rejected_summons
        # Allow spike shortcuts only when we carry a healing potion
        view.allow_spikes = _find_healing_item(view) is not None
        # One-way safe zone lock: once inside X [SAFE_X_LO, SAFE_X_HI], outer
        # columns are permanently forbidden so the bot never drifts back out.
        if view.my_pos and SAFE_X_LO <= view.my_pos[0] <= SAFE_X_HI:
            self._reached_safe_zone = True
        view.safe_zone_locked = self._reached_safe_zone
        view.last_was_attack  = self._last_was_attack

        if not view.me or not view.my_pos:
            self.log.warning("no self in state — skipping turn")
            return None

        label, action = self.pick_action(view)

        # Absolute last resort — guarantee we always send SOMETHING so the
        # server doesn't count this turn as a forfeit.
        if action is None and view.my_pos:
            for dx, dy in NEIGHBORS_4:
                nx, ny = view.my_pos[0] + dx, view.my_pos[1] + dy
                if view.in_bounds(nx, ny):
                    label, action = "last_resort", Move(nx, ny)
                    self.log.warning("last_resort fallback: Move(%s,%s)", nx, ny)
                    break

        if action is None:
            self.log.warning("no legal action found — passing")
            return None

        # Remember the intended real-world Move target *before* any inversion.
        # This is what gets tracked in rejected_targets so pathfinding stays
        # consistent regardless of whether Dizzy inversion is active.
        real_move_target: Optional[tuple] = (
            (action.x, action.y) if isinstance(action, Move) else None
        )

        # ── Confusion / Dizzy movement inversion ────────────────────────────
        # When the player is Dizzy the server reverses the movement direction.
        # To actually reach destination D from position P we must send the
        # mirror: request = 2*P - D.  This works even for out-of-bounds
        # values — the server applies the reversal and lands on D.
        if isinstance(action, Move) and view.has_status(S_DIZZY) and view.my_pos:
            mx, my = view.my_pos
            flipped = Move(2 * mx - action.x, 2 * my - action.y)
            self.log.info(
                "Dizzy active: want (%s,%s) -> sending (%s,%s)",
                action.x, action.y, flipped.x, flipped.y,
            )
            action = flipped

        if self.debug:
            self.log.info("\n%s", self.render_ascii(view))

        log_entry = {
            "turn": self.turn_count,
            "phase": _g(state, "GameState", "gameState"),
            "my_hp": view.my_hp, "enemy_hp": view.enemy_hp,
            "my_pos": list(view.my_pos),
            "enemy_pos": list(view.enemy_pos) if view.enemy_pos else None,
            "enemy_status": list(view.enemy_status.keys()),
            "enemy_inv": [_item_type_label(it) for it in view.enemy_inventory],
            "my_inv":    [_item_type_label(it) for it in view.my_inventory],
            "my_cards":  len(view.my_cards),
            "monsters":  len(view.monsters),
            "pickups":   len(view.pickups),
            "turns_to_shrink":      _turns_to_next_shrink(game_turn),
            "enemy_in_danger_zone": bool(view.enemy_pos and _is_in_danger_zone(view.enemy_pos[0])),
            "decision": label,
            "action":   action_to_dict(action),
        }
        self.log.info("%s", json.dumps(log_entry))
        self._dataset_log(state, label, action)
        self.history.append((self.turn_count, view.my_pos, label))

        self._last_was_attack = isinstance(action, Attack)
        self._last_action = action
        # Store the real (pre-inversion) target so rejected_targets always
        # tracks what we actually wanted to reach, not the flipped request.
        self._last_real_target: Optional[tuple] = real_move_target

        result = self.engine.execute(self.player_id, action)
        if result is None and isinstance(action, Move):
            # Use the real intended target, not the (possibly flipped) request
            rejected = real_move_target if real_move_target is not None else (action.x, action.y)
            self._rejected_targets.add(rejected)
            self.log.info("engine rejected Move, real target (%s,%s)", rejected[0], rejected[1])
        elif result is None and isinstance(action, Pickup):
            self._rejected_pickups.add((action.x, action.y))
            self.log.info("engine rejected Pickup(%s,%s)", action.x, action.y)
        elif result is None and isinstance(action, Summon):
            self._rejected_summons.add((action.card_id, action.x, action.y))
            self.log.info("engine rejected Summon(card=%s, %s,%s)", action.card_id, action.x, action.y)
        return result

    def play(self) -> None:
        while True:
            state = self.engine.get_state()
            if state and self.find_my_player_id(state):
                break
            time.sleep(0.5)
        self.log.info("connected as Player %s (%s)", self.player_id, self.bot_name)

        state = self.engine.get_state()
        try:
            while state and not self.is_game_over(state):
                if self.is_my_turn(state):
                    pre_phase = _g(state, "GameState", "gameState")
                    pre_turn  = _g(state, "TurnCounter", "turnCounter")
                    self.take_turn(state)
                    deadline = time.time() + 5.0
                    state_advanced = False
                    while time.time() < deadline:
                        time.sleep(POLL_INTERVAL)
                        fresh = self.engine.get_state()
                        if not fresh:
                            continue
                        state = fresh
                        if self.is_game_over(state):
                            state_advanced = True
                            break
                        if (_g(state, "GameState", "gameState") != pre_phase
                                or _g(state, "TurnCounter", "turnCounter") != pre_turn):
                            state_advanced = True
                            break
                    # Server accepted the request (HTTP 200) but didn't advance —
                    # treat the move target as rejected so we don't retry it.
                    if not state_advanced:
                        last      = getattr(self, "_last_action",      None)
                        last_real = getattr(self, "_last_real_target", None)
                        if isinstance(last, Move):
                            # Always track the real (pre-Dizzy-inversion) target
                            rejected = last_real if last_real is not None else (last.x, last.y)
                            self._rejected_targets.add(rejected)
                            self.log.info(
                                "state unchanged after Move, real target (%s,%s) — marking rejected",
                                rejected[0], rejected[1],
                            )
                        elif isinstance(last, Pickup):
                            self._rejected_pickups.add((last.x, last.y))
                            self.log.info(
                                "state unchanged after Pickup(%s,%s) — marking rejected",
                                last.x, last.y,
                            )
                        elif isinstance(last, Summon):
                            self._rejected_summons.add((last.card_id, last.x, last.y))
                            self.log.info(
                                "state unchanged after Summon(card=%s, %s,%s) — marking rejected",
                                last.card_id, last.x, last.y,
                            )
                else:
                    time.sleep(POLL_INTERVAL)
                    state = self.engine.get_state() or state
        except KeyboardInterrupt:
            self.log.info("stopped by user")
        finally:
            self.log.info("game over  phase=%s  turns_played=%s",
                          _g(state, "GameState", "gameState") if state else "?",
                          self.turn_count)


# ---------------------------------------------------------------------------
# Sanity tests  (--test)
# ---------------------------------------------------------------------------

def _make_fake_state(my_pos=(5, 5), enemy_pos=(6, 5),
                     my_hp=100, enemy_hp=100,
                     my_inv=None, enemy_inv=None,
                     my_status=None, enemy_status=None) -> dict:
    my_inv       = my_inv       or []
    enemy_inv    = enemy_inv    or []
    my_status    = my_status    or {}
    enemy_status = enemy_status or {}
    grid = [{"Position": {"X": x, "Y": y}, "FieldType": F_NORMAL,
             "Entity": None, "Item": None, "MonsterCard": None}
            for x in range(MAP_W) for y in range(MAP_H)]
    return {
        "GameState": "Player1Turn", "TurnCounter": 1,
        "Players": {
            "1": {"Id": 1, "Name": "A", "First": True,
                  "Health": my_hp, "MaxHealth": 100,
                  "AttackPower": 10, "AttackRange": 1, "MaxMoveDistance": 4,
                  "Position": {"X": my_pos[0], "Y": my_pos[1]},
                  "Inventory": my_inv, "Cards": [],
                  "ActiveStatuses": my_status,
                  "xPattern": [1, -1, 0, 0], "yPattern": [0, 0, 1, -1]},
            "2": {"Id": 2, "Name": "B", "First": False,
                  "Health": enemy_hp, "MaxHealth": 100,
                  "AttackPower": 10, "AttackRange": 1, "MaxMoveDistance": 4,
                  "Position": {"X": enemy_pos[0], "Y": enemy_pos[1]},
                  "Inventory": enemy_inv, "Cards": [],
                  "ActiveStatuses": enemy_status,
                  "xPattern": [1, -1, 0, 0], "yPattern": [0, 0, 1, -1]},
        },
        "Map": {"X": MAP_W, "Y": MAP_H, "Name": "test", "Grid": grid},
    }


def run_tests() -> int:
    print("running sanity checks...")

    assert manhattan((0, 0), (3, 4)) == 7
    assert manhattan((1, 1), (1, 1)) == 0

    # --- Basic view ---
    state = _make_fake_state()
    view  = GameView(state, my_id=1, turn_count=1)
    assert view.my_pos    == (5, 5)
    assert view.enemy_pos == (6, 5)
    assert view.field_type(0, 0) == F_NORMAL
    assert view.is_walkable(10, 10)

    # --- Lethal ---
    v2 = GameView(_make_fake_state(enemy_hp=5), my_id=1, turn_count=1)
    a  = decide_lethal(v2)
    assert isinstance(a, Attack) and a.target_id == 2, a

    # --- Step toward ---
    nxt = step_toward(view, (5, 5), (15, 5), 4)
    assert nxt and manhattan(nxt, (15, 5)) < manhattan((5, 5), (15, 5))

    # --- Action dicts ---
    assert action_to_dict(Move(1, 2))     == {"type": "Move",   "x": 1, "y": 2}
    assert action_to_dict(Pickup(3, 4))["type"] == "Pickup"
    assert action_to_dict(Summon(7, 1, 2))["card"] == 7

    # --- Zone trap ---
    freeze_item = {"Id": 99, "Name": "Freeze Scroll", "ItemType": "CrowdControl",
                   "Effect": "freeze", "Uses": 1}
    s3 = _make_fake_state(enemy_pos=(1, 8), my_inv=[freeze_item])
    s3["TurnCounter"] = 13   # 15 - 13 = 2 turns to shrink
    v3 = GameView(s3, my_id=1, turn_count=13)
    a3 = decide_zone_trap(v3)
    assert isinstance(a3, UseItem) and a3.item_id == 99, a3

    # --- Contest pickup ---
    freeze_item2 = {"Id": 99, "Name": "Freeze Scroll", "ItemType": "CrowdControl",
                    "Effect": "freeze", "Uses": 1}
    s4 = _make_fake_state(my_pos=(5, 5), enemy_pos=(15, 5), my_inv=[freeze_item2])
    # Place a freeze scroll equidistant at (10, 5)
    for f in s4["Map"]["Grid"]:
        if f["Position"] == {"X": 10, "Y": 5}:
            f["Item"] = {"Id": 77, "Name": "Freeze Scroll", "ItemType": "CrowdControl",
                         "Effect": "freeze"}
    v4 = GameView(s4, my_id=1, turn_count=1)
    a4 = decide_contest_pickup(v4)
    assert isinstance(a4, UseItem), f"expected UseItem, got {a4}"

    # --- Pickup priority scoring ---
    heal_item = {"Id": 50, "Name": "Healing Potion", "ItemType": "Healing", "Effect": "heal"}
    conf_item = {"Id": 51, "Name": "Confusion Scroll", "ItemType": "CrowdControl", "Effect": "confusion"}
    assert _pickup_priority_score({"Item": freeze_item}, 1.0) == 0
    assert _pickup_priority_score({"Item": conf_item},   1.0) == 0
    assert _pickup_priority_score({"MonsterCard": {"Id": 1, "Name": "IceCube"}}, 1.0) == 2
    assert _pickup_priority_score({"Item": heal_item},   0.2) == 3
    assert _pickup_priority_score({"Item": heal_item},   1.0) == 6

    # --- Snow walkable, spikes not (without healing) ---
    s5 = _make_fake_state()
    for f in s5["Map"]["Grid"]:
        if f["Position"] == {"X": 3, "Y": 3}: f["FieldType"] = F_OBSTACLE_SLOW
        if f["Position"] == {"X": 4, "Y": 4}: f["FieldType"] = F_OBSTACLE
    v5 = GameView(s5, my_id=1, turn_count=1)
    assert v5.is_walkable(3, 3),      "snow must be walkable"
    assert not v5.is_walkable(4, 4),  "spike not walkable without heal"
    v5.allow_spikes = True
    assert v5.is_walkable(4, 4),      "spike walkable with allow_spikes"

    # --- Shrink helpers ---
    assert _turns_to_next_shrink(1)  == 14
    assert _turns_to_next_shrink(14) == 1
    assert _turns_to_next_shrink(15) == 15
    assert _turns_to_next_shrink(16) == 14
    assert _is_in_danger_zone(0)
    assert _is_in_danger_zone(2)
    assert not _is_in_danger_zone(3)
    assert not _is_in_danger_zone(28)
    assert _is_in_danger_zone(29)
    assert _is_in_danger_zone(31)

    print("OK — all sanity checks passed.")
    return 0


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> int:
    if "--test" in sys.argv:
        return run_tests()
    if len(sys.argv) < 4:
        print("Usage: python ivan_bot.py <server_url> <game_id> <bot_name> [--debug]")
        return 1
    server_url, game_id, bot_name = sys.argv[1], sys.argv[2], sys.argv[3]
    debug = "--debug" in sys.argv
    MonsterHuntBot(server_url, game_id, bot_name, debug=debug).play()
    return 0


if __name__ == "__main__":
    sys.exit(main())
