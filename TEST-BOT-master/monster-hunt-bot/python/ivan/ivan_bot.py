"""Monster Hunt Bot — priority-based heuristic AI.

Usage:
    python bot_template_py.py <server_url> <game_id> <bot_name> [--debug] [--test]

See README.md for the engine API. Strategy notes live in
~/.claude/plans/napravi-detaljan-plan-i-polymorphic-pie.md
"""
from __future__ import annotations

import json
import logging
import os
import random
import sys
import time
from dataclasses import dataclass, field as dc_field
from typing import Any, Iterable, Optional, Union

import requests

# ─── Constants ────────────────────────────────────────────────────────────────

POLL_INTERVAL = 0.2
HTTP_TIMEOUT = 5.0
MAX_TURNS = 100
MAP_W = 32
MAP_H = 16
CENTER = (MAP_W // 2, MAP_H // 2)

LOW_HP_RATIO = 0.30
HEAL_HP_RATIO = 0.55
LATE_GAME_TURN = 70
ANTI_STUCK_WINDOW = 4

# Field types — mirror Models/Field.cs
F_BASE = 0
F_NORMAL = 1
F_OBSTACLE_SLOW = 2
F_OBSTACLE = 3
F_POWERUP = 4
F_WALL = 5
F_EMPTY = 6

# Slow obstacles are technically walkable for the engine (cost 2), but
# empirically once a player ENDS a move on one, every move attempt the
# next turn is rejected with no status flag set. We therefore treat them
# as non-walkable so the bot routes around them entirely.
WALKABLE = {F_BASE, F_NORMAL, F_POWERUP}
DEAD_TERRAIN = {F_WALL, F_EMPTY, F_OBSTACLE, F_OBSTACLE_SLOW}

# Status enum from Models/Enums/Status.cs
S_FROZEN = "Frozen"
S_FATIGUE = "Fatigue"
S_DIZZY = "Dizzy"

NEIGHBORS_4 = ((1, 0), (-1, 0), (0, 1), (0, -1))

# ─── Actions ──────────────────────────────────────────────────────────────────

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

@dataclass
class Summon:
    card_id: int
    x: int
    y: int

Action = Union[Move, Attack, UseItem, Pickup, Summon]


def action_to_dict(a: Action) -> dict:
    if isinstance(a, Move):
        return {"type": "Move", "x": a.x, "y": a.y}
    if isinstance(a, Attack):
        return {"type": "Attack", "target": a.target_id}
    if isinstance(a, UseItem):
        return {"type": "UseItem", "item": a.item_id}
    if isinstance(a, Pickup):
        return {"type": "Pickup", "x": a.x, "y": a.y}
    if isinstance(a, Summon):
        return {"type": "Summon", "card": a.card_id, "x": a.x, "y": a.y}
    return {"type": "Unknown"}


# ─── HTTP layer ───────────────────────────────────────────────────────────────

class Engine:
    def __init__(self, server_url: str, game_id: str, log: logging.Logger):
        self.url = server_url.rstrip("/")
        self.gid = game_id
        self.log = log

    def _req(self, method: str, path: str, body: Optional[dict] = None) -> Optional[dict]:
        url = f"{self.url}{path}"
        # Mutating endpoints must not auto-retry: a retry after a server-side
        # "Not your turn" can re-submit the move on the *next* phase and
        # corrupt our turn (we saw this produce 500/409 spam).
        attempts = (1,) if method != "GET" else (1, 2)
        for attempt in attempts:
            try:
                r = requests.request(method, url, json=body, timeout=HTTP_TIMEOUT)
                if 200 <= r.status_code < 300:
                    try:
                        return r.json()
                    except ValueError:
                        return {"_raw": r.text}
                self.log.warning("HTTP %s %s -> %s body=%s", method, path, r.status_code, r.text[:200])
                if 400 <= r.status_code < 600:
                    return None  # client/server error — retry won't help on a turn action
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
            return self._req("PUT", f"/map/pickup/{player_id}/gameId/{gid}",
                             {"Position": {"X": a.x, "Y": a.y}})
        if isinstance(a, Summon):
            return self._req("PUT", f"/map/{player_id}/summon/{a.card_id}/gameId/{gid}",
                             {"X": a.x, "Y": a.y})
        return None


# ─── State helpers (case-insensitive accessors) ───────────────────────────────

def _g(d: Optional[dict], *keys: str, default=None):
    """Get the first present key from a dict (case variants)."""
    if not d:
        return default
    for k in keys:
        if k in d:
            return d[k]
    return default


def _pos(p: Optional[dict]) -> Optional[tuple[int, int]]:
    if not p:
        return None
    x = _g(p, "X", "x")
    y = _g(p, "Y", "y")
    if x is None or y is None:
        return None
    return (int(x), int(y))


# ─── GameView: parsing wrapper around raw state ───────────────────────────────

class GameView:
    """Snapshot of the game, post-parsed for decision functions."""

    def __init__(self, state: dict, my_id: int, turn_count: int, tie_seed: int = 0):
        self.raw = state
        self.my_id = my_id
        self.turn_count = turn_count
        self.tie_seed = tie_seed

        players_dict = _g(state, "Players", "players", default={}) or {}
        # Players may be keyed by str(id) or int(id)
        players = list(players_dict.values()) if isinstance(players_dict, dict) else list(players_dict)
        self.me: Optional[dict] = None
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
        self.map_w = int(_g(self.map_obj, "X", "x", default=MAP_W))
        self.map_h = int(_g(self.map_obj, "Y", "y", default=MAP_H))
        self.grid: list = _g(self.map_obj, "Grid", "grid", default=[]) or []

        # Build lookup by (x,y) using stored Position when present, fallback to index math.
        self._field_by_xy: dict[tuple[int, int], dict] = {}
        for idx, f in enumerate(self.grid):
            if not isinstance(f, dict):
                continue
            p = _pos(_g(f, "Position", "position"))
            if p is None:
                # Reverse engine indexing: Grid[X * map.Y + Y]
                x = idx // self.map_h
                y = idx % self.map_h
                p = (x, y)
            self._field_by_xy[p] = f

        self.monsters: list[dict] = []
        self.pickups: list[tuple[tuple[int, int], dict]] = []
        for (x, y), f in self._field_by_xy.items():
            ent = _g(f, "Entity", "entity")
            if ent and isinstance(ent, dict):
                # JSON $ref entries are back-references to the players defined
                # elsewhere in the state — they don't have an Id field and
                # would otherwise be miscounted as monsters.
                if "$ref" in ent or "$Ref" in ent:
                    pass
                else:
                    eid = _g(ent, "Id", "id")
                    if eid is not None and eid != my_id and (
                            self.enemy is None or eid != _g(self.enemy, "Id", "id")):
                        self.monsters.append(ent)
            if _g(f, "Item", "item") or _g(f, "MonsterCard", "monsterCard"):
                self.pickups.append(((x, y), f))

        # Convenience
        self.my_pos = _pos(_g(self.me, "Position", "position")) if self.me else None
        self.enemy_pos = _pos(_g(self.enemy, "Position", "position")) if self.enemy else None
        self.my_hp = int(_g(self.me, "Health", "health", default=0)) if self.me else 0
        self.my_max_hp = int(_g(self.me, "MaxHealth", "maxHealth", default=100)) if self.me else 100
        self.enemy_hp = int(_g(self.enemy, "Health", "health", default=0)) if self.enemy else 0
        self.my_atk = int(_g(self.me, "AttackPower", "attackPower", default=10)) if self.me else 10
        self.my_range = int(_g(self.me, "AttackRange", "attackRange", default=1)) if self.me else 1
        self.my_move = int(_g(self.me, "MaxMoveDistance", "maxMoveDistance", default=4)) if self.me else 4
        self.my_inventory: list = _g(self.me, "Inventory", "inventory", default=[]) or [] if self.me else []
        self.my_cards: list = _g(self.me, "Cards", "cards", default=[]) or [] if self.me else []
        self.my_status: dict = _g(self.me, "ActiveStatuses", "activeStatuses", default={}) or {} if self.me else {}

    def field(self, x: int, y: int) -> Optional[dict]:
        return self._field_by_xy.get((x, y))

    def field_type(self, x: int, y: int) -> int:
        f = self.field(x, y)
        if not f:
            return F_EMPTY
        ft = _g(f, "FieldType", "fieldType", default=F_EMPTY)
        return int(ft) if ft is not None else F_EMPTY

    def in_bounds(self, x: int, y: int) -> bool:
        return 0 <= x < self.map_w and 0 <= y < self.map_h

    def is_walkable(self, x: int, y: int, ignore_entities: bool = False) -> bool:
        if not self.in_bounds(x, y):
            return False
        if self.field_type(x, y) not in WALKABLE:
            return False
        if not ignore_entities:
            f = self.field(x, y)
            if f and _g(f, "Entity", "entity"):
                return False
        return True

    def has_status(self, name: str) -> bool:
        if not self.my_status:
            return False
        # Status dict may use enum-name keys ("Frozen") or int values
        for k in self.my_status.keys():
            if str(k).lower() == name.lower():
                return True
        return False


# ─── Geometry / pathfinding ───────────────────────────────────────────────────

def manhattan(a: tuple[int, int], b: tuple[int, int]) -> int:
    return abs(a[0] - b[0]) + abs(a[1] - b[1])


def attack_cells(attacker: dict) -> set[tuple[int, int]]:
    """Cells the attacker can hit, derived from xPattern/yPattern * AttackRange.

    Pattern offsets are unit directions; each step from 1..AttackRange yields
    a hit cell. Falls back to 4-neighborhood if pattern arrays are missing.
    """
    pos = _pos(_g(attacker, "Position", "position"))
    if not pos:
        return set()
    xp = _g(attacker, "xPattern", default=None) or [1, -1, 0, 0]
    yp = _g(attacker, "yPattern", default=None) or [0, 0, 1, -1]
    rng = max(1, int(_g(attacker, "AttackRange", "attackRange", default=1) or 1))
    cells: set[tuple[int, int]] = set()
    for dx, dy in zip(xp, yp):
        for r in range(1, rng + 1):
            cells.add((pos[0] + dx * r, pos[1] + dy * r))
    return cells


def reachable_cells(view: GameView, start: tuple[int, int],
                    max_dist: Optional[int] = None) -> dict[tuple[int, int], int]:
    """BFS over walkable, entity-free cells. ``max_dist=None`` means unlimited."""
    seen = {start: 0}
    queue = [start]
    while queue:
        cur = queue.pop(0)
        d = seen[cur]
        if max_dist is not None and d >= max_dist:
            continue
        for dx, dy in NEIGHBORS_4:
            nx, ny = cur[0] + dx, cur[1] + dy
            if (nx, ny) in seen:
                continue
            if not view.is_walkable(nx, ny, ignore_entities=False):
                continue
            seen[(nx, ny)] = d + 1
            queue.append((nx, ny))
    return seen


def bfs_from_goal(view: GameView, goal: tuple[int, int]) -> dict[tuple[int, int], int]:
    """Distances from ``goal`` to every walkable cell.

    The goal itself may be occupied (e.g. the enemy player) — we still seed
    the search there so neighbours get distance 1.
    """
    seen = {goal: 0}
    queue = [goal]
    while queue:
        cur = queue.pop(0)
        d = seen[cur]
        for dx, dy in NEIGHBORS_4:
            nx, ny = cur[0] + dx, cur[1] + dy
            if (nx, ny) in seen:
                continue
            if not view.is_walkable(nx, ny, ignore_entities=False):
                continue
            seen[(nx, ny)] = d + 1
            queue.append((nx, ny))
    return seen


def single_axis_moves(view: GameView, start: tuple[int, int],
                      max_dist: int) -> list[tuple[int, int]]:
    """All legal straight-line move targets from ``start``.

    Slow obstacles are excluded by ``is_walkable`` (see ``WALKABLE`` for the
    rationale), so every walkable cell costs exactly one move point.
    """
    rejected: set[tuple[int, int]] = getattr(view, "rejected_targets", set()) or set()
    out: list[tuple[int, int]] = []
    for dx, dy in NEIGHBORS_4:
        for k in range(1, max_dist + 1):
            cx, cy = start[0] + dx * k, start[1] + dy * k
            if not view.is_walkable(cx, cy):
                break
            if (cx, cy) not in rejected:
                out.append((cx, cy))
    return out


def step_toward(view: GameView, start: tuple[int, int], goal: tuple[int, int],
                max_dist: int) -> Optional[tuple[int, int]]:
    """Pick the best legal single-axis move toward ``goal``.

    The engine only accepts straight-line moves (one of X or Y changes), so
    we enumerate every (direction, step-length) pair, drop any with a blocked
    intermediate cell, then rank by BFS path distance to ``goal`` so the bot
    routes around obstacles instead of stalling in front of them.
    """
    dist_from_goal = bfs_from_goal(view, goal)
    big = view.map_w * view.map_h + 1
    seed = getattr(view, "tie_seed", 0)

    candidates = single_axis_moves(view, start, max_dist)
    if not candidates:
        return None

    def score(c: tuple[int, int]) -> tuple:
        d_goal = dist_from_goal.get(c, big + manhattan(c, goal))
        # Tertiary: per-bot jitter so symmetric self-play doesn't mirror.
        return (d_goal, hash((c, seed)) & 0xFFFF)

    return min(candidates, key=score)


# ─── Decision functions (priority pipeline) ───────────────────────────────────

def _enemy_in_range(view: GameView) -> bool:
    if not view.me or not view.enemy or not view.enemy_pos:
        return False
    return view.enemy_pos in attack_cells(view.me)


def _monster_in_range(view: GameView) -> Optional[dict]:
    if not view.me:
        return None
    cells = attack_cells(view.me)
    best = None
    best_hp = 10**9
    for m in view.monsters:
        mp = _pos(_g(m, "Position", "position"))
        if mp and mp in cells:
            hp = int(_g(m, "Health", "health", default=10**9))
            if hp < best_hp:
                best_hp = hp
                best = m
    return best


def decide_lethal(view: GameView) -> Optional[Action]:
    if _enemy_in_range(view) and view.my_atk >= view.enemy_hp > 0:
        return Attack(int(_g(view.enemy, "Id", "id")))
    return None


def _find_item(view: GameView, predicate) -> Optional[dict]:
    for it in view.my_inventory:
        if predicate(it):
            return it
    return None


def decide_emergency(view: GameView) -> Optional[Action]:
    if not view.me or not view.my_pos:
        return None
    if view.my_hp <= 0 or view.my_max_hp <= 0:
        return None
    ratio = view.my_hp / max(1, view.my_max_hp)
    if ratio >= LOW_HP_RATIO:
        return None
    # 1) Use a healing item if we have one
    heal = _find_item(view, lambda it: str(_g(it, "ItemType", "itemType", "Effect", default="")).lower() in
                      ("healing", "heal") or "heal" in str(_g(it, "Effect", "effect", default="")).lower())
    if heal is not None:
        return UseItem(int(_g(heal, "Id", "id")))
    # 2) Otherwise retreat toward centre, away from enemy
    target = CENTER
    if view.enemy_pos:
        # mirror across self
        tx = view.my_pos[0] + (view.my_pos[0] - view.enemy_pos[0])
        ty = view.my_pos[1] + (view.my_pos[1] - view.enemy_pos[1])
        target = (max(2, min(view.map_w - 3, tx)), max(2, min(view.map_h - 3, ty)))
    nxt = step_toward(view, view.my_pos, target, view.my_move)
    if nxt:
        return Move(nxt[0], nxt[1])
    return None


def decide_edge_safety(view: GameView) -> Optional[Action]:
    """If standing near shrinking-map dead terrain, retreat inward."""
    if view.has_status(S_FROZEN):
        return None
    if not view.my_pos:
        return None
    x, y = view.my_pos
    # Count dead-terrain neighbors within 2 cells (radius — covers near edges)
    dead = 0
    total = 0
    for dx in range(-2, 3):
        for dy in range(-2, 3):
            if dx == 0 and dy == 0:
                continue
            nx, ny = x + dx, y + dy
            total += 1
            if not view.in_bounds(nx, ny) or view.field_type(nx, ny) in DEAD_TERRAIN:
                dead += 1
    # Also: am I literally on a vanishing tile?
    on_dead = view.field_type(x, y) in DEAD_TERRAIN
    if not on_dead and dead / max(1, total) < 0.45:
        return None
    nxt = step_toward(view, view.my_pos, CENTER, view.my_move)
    if nxt and nxt != view.my_pos:
        return Move(nxt[0], nxt[1])
    return None


def decide_opportunity(view: GameView) -> Optional[Action]:
    if _enemy_in_range(view) and view.enemy_hp > 0:
        # Attack if it's a meaningful chunk or we can finish quickly
        if view.my_atk * 3 >= view.enemy_hp or view.my_hp > view.enemy_hp:
            return Attack(int(_g(view.enemy, "Id", "id")))
    mon = _monster_in_range(view)
    if mon is not None:
        mhp = int(_g(mon, "Health", "health", default=0))
        if mhp <= view.my_atk * 2:  # easy kill / xp farm
            return Attack(int(_g(mon, "Id", "id")))
    return None


def decide_pickup(view: GameView) -> Optional[Action]:
    if not view.my_pos or not view.pickups:
        return None
    # The engine accepts Pickup only when the player is standing on the cell.
    for (x, y), f in view.pickups:
        if (x, y) == view.my_pos:
            return Pickup(x, y)
    # If an item is adjacent, step onto it so we can pick it up next turn.
    for (x, y), f in view.pickups:
        if manhattan((x, y), view.my_pos) == 1 and view.is_walkable(x, y):
            return Move(x, y)
    return None


def decide_summon(view: GameView) -> Optional[Action]:
    if not view.my_cards or not view.my_pos or not view.enemy_pos:
        return None
    # Find a non-cooldown card with the highest monster ATK
    usable = []
    for c in view.my_cards:
        on_cd = bool(_g(c, "OnCooldown", "onCooldown", default=False))
        cd_counter = int(_g(c, "CooldownCounter", "cooldownCounter", default=0) or 0)
        cd = int(_g(c, "Cooldown", "cooldown", default=0) or 0)
        if on_cd and cd_counter < cd:
            continue
        mon = _g(c, "Monster", "monster") or {}
        atk = int(_g(mon, "AttackPower", "attackPower", default=0) or 0)
        usable.append((atk, c))
    if not usable:
        return None
    usable.sort(key=lambda t: -t[0])
    card = usable[0][1]
    card_id = int(_g(card, "Id", "id"))
    # Try to summon adjacent to enemy (squeeze them)
    ex, ey = view.enemy_pos
    candidates: list[tuple[int, int, int]] = []  # (dist_to_enemy, x, y)
    for dx, dy in NEIGHBORS_4:
        sx, sy = ex + dx, ey + dy
        if view.is_walkable(sx, sy):
            candidates.append((0, sx, sy))
    # Otherwise summon adjacent to me (frontline)
    if not candidates:
        mx, my = view.my_pos
        for dx, dy in NEIGHBORS_4:
            sx, sy = mx + dx, my + dy
            if view.is_walkable(sx, sy):
                candidates.append((manhattan((sx, sy), view.enemy_pos), sx, sy))
    if not candidates:
        return None
    candidates.sort()
    _, sx, sy = candidates[0]
    return Summon(card_id, sx, sy)


def decide_powerup(view: GameView) -> Optional[Action]:
    if not view.my_pos:
        return None
    cells = reachable_cells(view, view.my_pos, view.my_move)
    best: Optional[tuple[int, tuple[int, int]]] = None
    for cell, dist in cells.items():
        if cell == view.my_pos:
            continue
        if view.field_type(*cell) == F_POWERUP:
            score = dist  # closer is better
            if best is None or score < best[0]:
                best = (score, cell)
    if best:
        return Move(best[1][0], best[1][1])
    return None


def decide_pressure(view: GameView) -> Optional[Action]:
    if view.has_status(S_FROZEN):
        return None
    if not view.my_pos or not view.enemy_pos:
        return None

    target = view.enemy_pos
    # Late game push toward the edge if we're ahead on HP
    if view.turn_count >= LATE_GAME_TURN and view.my_hp > view.enemy_hp:
        ex, ey = view.enemy_pos
        # Pick the edge closer to enemy, pull them further out
        push_x = 0 if ex < view.map_w // 2 else view.map_w - 1
        push_y = ey  # bias horizontally; map is wider than tall
        target = (push_x, push_y)

    nxt = step_toward(view, view.my_pos, target, view.my_move)
    if nxt and nxt != view.my_pos:
        return Move(nxt[0], nxt[1])
    # Fallback: longest legal straight-line move so we don't stand still.
    moves = single_axis_moves(view, view.my_pos, view.my_move)
    if moves:
        c = max(moves, key=lambda c: manhattan(c, view.my_pos))
        return Move(c[0], c[1])
    return None


def decide_fallback(view: GameView) -> Optional[Action]:
    if view.has_status(S_FROZEN) or not view.my_pos:
        return None
    nxt = step_toward(view, view.my_pos, CENTER, view.my_move)
    if nxt and nxt != view.my_pos:
        return Move(nxt[0], nxt[1])
    # Random walkable neighbor
    rng = random.Random(hash(view.my_pos) & 0xFFFF)
    options = list(NEIGHBORS_4)
    rng.shuffle(options)
    for dx, dy in options:
        nx, ny = view.my_pos[0] + dx, view.my_pos[1] + dy
        if view.is_walkable(nx, ny):
            return Move(nx, ny)
    return None


PRIORITIES = [
    ("lethal", decide_lethal),
    ("emergency", decide_emergency),
    ("edge_safety", decide_edge_safety),
    ("opportunity", decide_opportunity),
    ("pickup", decide_pickup),
    ("summon", decide_summon),
    ("powerup", decide_powerup),
    ("pressure", decide_pressure),
    ("fallback", decide_fallback),
]


# ─── Bot orchestration ────────────────────────────────────────────────────────

class MonsterHuntBot:
    def __init__(self, server_url: str, game_id: str, bot_name: str, debug: bool = False):
        self.bot_name = bot_name
        self.game_id = game_id
        self.debug = debug
        self.player_id: Optional[int] = None
        self.turn_count = 0
        # Stable per-bot seed so symmetric self-play doesn't mirror — used
        # for path tiebreaks in step_toward via GameView.tie_seed.
        self._tie_seed = hash(bot_name) & 0xFFFF
        self.history: list[tuple[int, tuple[int, int], str]] = []
        # Move targets the engine refused this turn — skipped on retry so we
        # don't pick the same invalid move again (e.g. an obstacle layout we
        # mis-modelled). Cleared on phase/turn change.
        self._rejected_targets: set[tuple[int, int]] = set()
        self._turn_key: Optional[tuple] = None
        self.log = self._setup_logger(bot_name)
        self.engine = Engine(server_url, game_id, self.log)
        # Per-game JSONL dataset file — appended (multiple bots in the same
        # game share the file; bot_name disambiguates rows).
        logs_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logs")
        os.makedirs(logs_dir, exist_ok=True)
        self.dataset_path = os.path.join(logs_dir, f"{game_id}.txt")

    def _dataset_log(self, state: dict, label: str, action: "Action") -> None:
        record = {
            "ts": time.time(),
            "game_id": self.game_id,
            "bot_name": self.bot_name,
            "player_id": self.player_id,
            "turn": self.turn_count,
            "phase": _g(state, "GameState", "gameState"),
            "decision": label,
            "action": action_to_dict(action),
            "state": state,
        }
        try:
            with open(self.dataset_path, "a", encoding="utf-8") as f:
                f.write(json.dumps(record, separators=(",", ":")) + "\n")
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
        items = players.values() if isinstance(players, dict) else players
        for p in items:
            if not p:
                continue
            if _g(p, "Name", "name") == self.bot_name:
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
        gs = _g(state, "GameState", "gameState", default="")
        return (first and gs == "Player1Turn") or (not first and gs == "Player2Turn")

    def is_game_over(self, state: dict) -> bool:
        return bool(state) and _g(state, "GameState", "gameState", default="") == "Ending"

    def _is_stuck(self) -> bool:
        if len(self.history) < ANTI_STUCK_WINDOW:
            return False
        recent = self.history[-ANTI_STUCK_WINDOW:]
        positions = {h[1] for h in recent}
        actions = {h[2] for h in recent}
        no_combat = not any(a in ("lethal", "opportunity") for a in actions)
        # Stuck = bouncing in <=2 cells with no fighting (catches oscillation)
        return len(positions) <= 2 and no_combat

    def _unstick(self, view: GameView) -> Optional[Action]:
        if not view.my_pos:
            return None
        candidates = single_axis_moves(view, view.my_pos, view.my_move)
        if not candidates:
            return None
        # Pick the farthest legal move biased perpendicular to the enemy
        # axis so we step *around* whatever obstacle we're stuck on, then
        # use a per-bot/per-turn jitter to break the symmetry that caused
        # the loop in the first place.
        mx, my = view.my_pos
        if view.enemy_pos:
            ex, ey = view.enemy_pos
            horiz_axis = abs(ex - mx) >= abs(ey - my)
        else:
            horiz_axis = True
        seed = (hash(self.bot_name) ^ self.turn_count) & 0xFFFF

        def score(c: tuple[int, int]) -> tuple:
            steps = manhattan(c, view.my_pos)
            perp = abs(c[1] - my) if horiz_axis else abs(c[0] - mx)
            return (-steps, -perp, hash((c, seed)) & 0xFFFF)

        candidates.sort(key=score)
        nx, ny = candidates[0]
        return Move(nx, ny)

    def pick_action(self, view: GameView) -> tuple[str, Optional[Action]]:
        chosen_label = "none"
        chosen: Optional[Action] = None
        if self._is_stuck():
            a = self._unstick(view)
            if a is not None:
                return ("anti_stuck", a)
        for label, fn in PRIORITIES:
            try:
                a = fn(view)
            except Exception as e:  # decision functions must never crash the bot
                self.log.exception("decision %s threw: %s", label, e)
                a = None
            if a is not None:
                return (label, a)
        return (chosen_label, chosen)

    def render_ascii(self, view: GameView) -> str:
        rows = []
        for y in range(view.map_h - 1, -1, -1):
            row = []
            for x in range(view.map_w):
                ch = "."
                ft = view.field_type(x, y)
                if ft == F_WALL:
                    ch = "#"
                elif ft == F_EMPTY:
                    ch = " "
                elif ft == F_OBSTACLE:
                    ch = "x"
                elif ft == F_OBSTACLE_SLOW:
                    ch = "~"
                elif ft == F_POWERUP:
                    ch = "+"
                elif ft == F_BASE:
                    ch = "B"
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

    def take_turn(self, state: dict) -> Optional[dict]:
        self.turn_count += 1
        # Engine's authoritative turn counter — used for late-game logic so
        # LATE_GAME_TURN doesn't trigger at half the real game time.
        game_turn = int(_g(state, "TurnCounter", "turnCounter", default=self.turn_count) or self.turn_count)
        # Reset per-turn rejection memory whenever the engine actually advances.
        turn_key = (game_turn, _g(state, "GameState", "gameState"))
        if turn_key != self._turn_key:
            self._rejected_targets.clear()
            self._turn_key = turn_key
        view = GameView(state, self.player_id, game_turn, tie_seed=self._tie_seed)
        view.rejected_targets = self._rejected_targets
        if not view.me or not view.my_pos:
            self.log.warning("no self in state — skipping turn")
            return None

        label, action = self.pick_action(view)
        if action is None:
            self.log.warning("no legal action found — passing")
            return None

        if self.debug:
            self.log.info("\n%s", self.render_ascii(view))

        log_entry = {
            "turn": self.turn_count,
            "phase": _g(state, "GameState", "gameState"),
            "my_hp": view.my_hp,
            "enemy_hp": view.enemy_hp,
            "my_pos": list(view.my_pos),
            "enemy_pos": list(view.enemy_pos) if view.enemy_pos else None,
            "monsters": len(view.monsters),
            "pickups": len(view.pickups),
            "decision": label,
            "action": action_to_dict(action),
        }
        self.log.info("%s", json.dumps(log_entry))
        self._dataset_log(state, label, action)
        self.history.append((self.turn_count, view.my_pos, label))

        result = self.engine.execute(self.player_id, action)
        # If the engine refused this Move, remember the target so we don't
        # re-pick it on the same turn-key. Cleared on phase/turn change.
        if result is None and isinstance(action, Move):
            self._rejected_targets.add((action.x, action.y))
            self.log.info("engine rejected Move(%s,%s) — blocking for this turn", action.x, action.y)
        return result

    def play(self) -> None:
        # Bootstrap: wait for game + discover ID
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
                    pre_turn = _g(state, "TurnCounter", "turnCounter")
                    self.take_turn(state)
                    # Block until the engine actually advances past this turn.
                    # Polling on stale state and re-firing a move is what
                    # produces the 409 "Action Already Processing" spam.
                    deadline = time.time() + 5.0
                    while time.time() < deadline:
                        time.sleep(POLL_INTERVAL)
                        fresh = self.engine.get_state()
                        if not fresh:
                            continue
                        state = fresh
                        if self.is_game_over(state):
                            break
                        cur_phase = _g(state, "GameState", "gameState")
                        cur_turn = _g(state, "TurnCounter", "turnCounter")
                        if cur_phase != pre_phase or cur_turn != pre_turn:
                            break
                else:
                    time.sleep(POLL_INTERVAL)
                    state = self.engine.get_state() or state
        except KeyboardInterrupt:
            self.log.info("stopped by user")
        finally:
            self.log.info("game over (final phase=%s, turns_played=%s)",
                          _g(state, "GameState", "gameState") if state else "unknown",
                          self.turn_count)


# ─── Sanity unit checks (--test) ──────────────────────────────────────────────

def run_tests() -> int:
    print("running sanity checks...")
    assert manhattan((0, 0), (3, 4)) == 7
    assert manhattan((1, 1), (1, 1)) == 0

    fake = {
        "GameState": "Player1Turn",
        "Players": {
            "1": {"Id": 1, "Name": "A", "First": True, "Health": 100, "MaxHealth": 100,
                  "AttackPower": 10, "AttackRange": 1, "MaxMoveDistance": 4,
                  "Position": {"X": 5, "Y": 5}, "Inventory": [], "Cards": [],
                  "ActiveStatuses": {}, "xPattern": [1, -1, 0, 0], "yPattern": [0, 0, 1, -1]},
            "2": {"Id": 2, "Name": "B", "First": False, "Health": 5, "MaxHealth": 100,
                  "AttackPower": 10, "AttackRange": 1, "MaxMoveDistance": 4,
                  "Position": {"X": 6, "Y": 5}, "Inventory": [], "Cards": [],
                  "ActiveStatuses": {}, "xPattern": [1, -1, 0, 0], "yPattern": [0, 0, 1, -1]},
        },
        "Map": {
            "X": MAP_W, "Y": MAP_H, "Name": "test",
            "Grid": [{"Position": {"X": x, "Y": y}, "FieldType": F_NORMAL,
                      "Entity": None, "Item": None, "MonsterCard": None}
                     for x in range(MAP_W) for y in range(MAP_H)],
        },
    }
    view = GameView(fake, my_id=1, turn_count=1)
    assert view.my_pos == (5, 5), view.my_pos
    assert view.enemy_pos == (6, 5), view.enemy_pos
    assert view.field_type(0, 0) == F_NORMAL
    assert view.is_walkable(10, 10)

    # Lethal: enemy adjacent with 5 HP, ATK 10 → attack
    a = decide_lethal(view)
    assert isinstance(a, Attack) and a.target_id == 2, a

    # Step toward goal beyond move range
    nxt = step_toward(view, (5, 5), (15, 5), 4)
    assert nxt and manhattan(nxt, (15, 5)) < manhattan((5, 5), (15, 5)), nxt

    # Action JSON serialisation
    assert action_to_dict(Move(1, 2)) == {"type": "Move", "x": 1, "y": 2}
    assert action_to_dict(Pickup(3, 4))["type"] == "Pickup"
    assert action_to_dict(Summon(7, 1, 2))["card"] == 7

    # Pickup detection
    fake2 = json.loads(json.dumps(fake))
    fake2["Map"]["Grid"][5 * MAP_H + 5]["Item"] = {"Id": 99, "Name": "Heal", "ItemType": 1}
    view2 = GameView(fake2, my_id=1, turn_count=1)
    assert any(p[0] == (5, 5) for p in view2.pickups)
    pa = decide_pickup(view2)
    assert isinstance(pa, Pickup) and (pa.x, pa.y) == (5, 5), pa

    print("OK — all sanity checks passed.")
    return 0


# ─── main ─────────────────────────────────────────────────────────────────────

def main() -> int:
    if "--test" in sys.argv:
        return run_tests()
    if len(sys.argv) < 4:
        print("Usage: python bot_template_py.py <server_url> <game_id> <bot_name> [--debug]")
        return 1
    server_url, game_id, bot_name = sys.argv[1], sys.argv[2], sys.argv[3]
    debug = "--debug" in sys.argv
    bot = MonsterHuntBot(server_url, game_id, bot_name, debug=debug)
    bot.play()
    return 0


if __name__ == "__main__":
    sys.exit(main())
