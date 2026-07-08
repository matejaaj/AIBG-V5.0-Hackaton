"""Pathfinding utilities for Monster Hunt bot.

Provides:
- bfs(start, goal, game_map) -> (x,y) | None
- flood_fill(start, max_move, game_map) -> set((x,y))
- find_nearest(start, condition, game_map) -> (x,y) | None

Assumptions and defaults:
- Positions are (x, y) tuples, 0-based, integers.
- game_map is the JSON-like map object returned by the game state. It must
  contain a flat `Grid` list of length width*height. If width/height are not
  provided the defaults (32 x 16) are used.

These functions are pure and have no side effects.
"""
from typing import Tuple, Optional, Set, Callable, Dict, Any
import heapq

from search_core import SearchState, BreadthFirstSearch  # noqa: E402

__all__ = [
    "bfs", "flood_fill", "find_nearest",
    "find_path", "reachable", "distance", "set_active_map",
]

Position = Tuple[int, int]

# Field type constants
BASE = 0
NORMAL = 1
OBSTACLE_SLOW = 2
OBSTACLE = 3
POWERUP = 4
WALL = 5
EMPTY = 6

WALKABLE_TYPES = (BASE, NORMAL, OBSTACLE_SLOW, POWERUP)

# The "active map" lets the public helpers be called as find_path(me, goal)
# without threading the game map through every call. The bot sets it once per turn.
_ACTIVE_MAP: Optional[Dict[str, Any]] = None


def set_active_map(game_map: Optional[Dict[str, Any]]) -> None:
    """Bind the current turn's map so find_path/reachable/distance/find_nearest
    can be called without an explicit game_map argument."""
    global _ACTIVE_MAP
    _ACTIVE_MAP = game_map


def _resolve_map(game_map):
    return game_map if game_map is not None else _ACTIVE_MAP


def _sign(n: int) -> int:
    return (n > 0) - (n < 0)


def _pos(x) -> Optional[Position]:
    """Accept an (x, y) tuple/list or any object exposing `.position`; return a tuple."""
    if x is None:
        return None
    if isinstance(x, (tuple, list)):
        return (int(x[0]), int(x[1]))
    p = getattr(x, 'position', None)
    if p is not None:
        if isinstance(p, (tuple, list)):
            return (int(p[0]), int(p[1]))
        px, py = getattr(p, 'X', None), getattr(p, 'Y', None)
        if px is not None and py is not None:
            return (int(px), int(py))
    raise TypeError(f"Cannot interpret {x!r} as a position")


def _max_move_of(x, default: int = 4) -> int:
    mm = getattr(x, 'max_move', None)
    if mm:
        return int(mm)
    return default


def _get_map_size(game_map: Dict[str, Any]) -> Tuple[int, int]:
    # Real game maps expose width/height as X/Y; fall back to other names, then 32x16.
    width = game_map.get('X') or game_map.get('Width') or game_map.get('width') or game_map.get('MapWidth')
    height = game_map.get('Y') or game_map.get('Height') or game_map.get('height') or game_map.get('MapHeight')
    if width and height:
        return int(width), int(height)
    return 32, 16


def _index(x: int, y: int, width: int, height: int) -> int:
    return x * height + y


def _get_field_type_at(game_map: Dict[str, Any], x: int, y: int, width: int, height: int) -> Optional[int]:
    if x < 0 or x >= width or y < 0 or y >= height:
        return None
    grid = game_map.get('Grid') or game_map.get('grid') or []
    idx = _index(x, y, width, height)
    if idx < 0 or idx >= len(grid):
        return None
    cell = grid[idx]
    # cell may be an int or a dict/object
    if isinstance(cell, dict):
        # common property name is FieldType
        if 'FieldType' in cell:
            return cell['FieldType']
        if 'field_type' in cell:
            return cell['field_type']
        if 'fieldType' in cell:
            return cell['fieldType']
    if isinstance(cell, int):
        return cell
    return None


def _get_cell_at(game_map: Dict[str, Any], x: int, y: int, width: int, height: int) -> Optional[Any]:
    if x < 0 or x >= width or y < 0 or y >= height:
        return None
    grid = game_map.get('Grid') or game_map.get('grid') or []
    idx = _index(x, y, width, height)
    if idx < 0 or idx >= len(grid):
        return None
    return grid[idx]


def _has_damage_obstacle(cell) -> bool:
    """Return True if the cell contains a damage-dealing obstacle (e.g. Spikes)."""
    if not isinstance(cell, dict):
        return False
    obs = cell.get('Obstacle') or cell.get('obstacle')
    if not obs or not isinstance(obs, dict):
        return False
    damage = obs.get('Damage') or obs.get('damage') or 0
    return int(damage) > 0


def _is_walkable(field_type: Optional[int], cell=None, avoid_spikes: bool = True) -> bool:
    if field_type not in (BASE, NORMAL, OBSTACLE_SLOW, POWERUP):
        return False
    if avoid_spikes and cell is not None and _has_damage_obstacle(cell):
        return False
    return True


def _movement_cost_for(field_type: Optional[int]) -> int:
    if field_type == OBSTACLE_SLOW:
        return 2
    # BASE, NORMAL, POWERUP
    return 1


def _neighbors(pos: Position, width: int, height: int):
    x, y = pos
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < width and 0 <= ny < height:
            yield nx, ny


def bfs(start: Position, goal: Position, game_map: Dict[str, Any],
        avoid_spikes: bool = False) -> Optional[Position]:
    """Return the first turn-state step from start towards goal.

    The search frontier is turn-based: one edge means one legal move this turn,
    where the move may cover 1..max_move tiles in a straight line. This keeps
    the BFS state model aligned with the bot's actual turn mechanics.
    If `avoid_spikes` is True, spike tiles are treated as impassable.
    """
    game_map = _resolve_map(game_map)
    if game_map is None:
        return None
    s = _pos(start)
    g = _pos(goal)
    if s is None or g is None or s == g:
        return None
    width, height = _get_map_size(game_map)
    if not (0 <= s[0] < width and 0 <= s[1] < height):
        return None
    if not (0 <= g[0] < width and 0 <= g[1] < height):
        return None

    root = TurnMoveState(game_map, s, _max_move_of(start), goal=g, avoid_spikes=avoid_spikes)
    path, _, _ = BreadthFirstSearch(game_map).search(root)
    if path and len(path) > 1:
        return path[1].position

    # Fallback: choose the legal one-turn move that gets closest to the goal.
    moves = root.get_next_states()
    if not moves:
        return None

    def score(pos):
        d = distance(pos, g, game_map)
        if d is None:
            d = 10**9
        return (d, abs(pos[0] - g[0]) + abs(pos[1] - g[1]), pos[0], pos[1])

    return min((state.position for state in moves), key=score)


def _flood_fill_core(start: Position, max_move: int, game_map: Dict[str, Any],
                     width: int, height: int, avoid_spikes: bool) -> Set[Position]:
    """Dijkstra flood fill up to max_move cost. Returns reachable position set."""
    frontier = []
    sx, sy = start
    heapq.heappush(frontier, (0, sx, sy))
    cost_so_far: Dict[Position, int] = {start: 0}
    reachable: Set[Position] = set()

    while frontier:
        cost, x, y = heapq.heappop(frontier)
        pos = (x, y)
        if cost > max_move:
            continue
        reachable.add(pos)

        for nx, ny in _neighbors(pos, width, height):
            field_type = _get_field_type_at(game_map, nx, ny, width, height)
            cell = _get_cell_at(game_map, nx, ny, width, height)
            if not _is_walkable(field_type, cell, avoid_spikes):
                continue
            move_cost = _movement_cost_for(field_type)
            new_cost = cost + move_cost
            neighbor = (nx, ny)
            if new_cost <= max_move and (neighbor not in cost_so_far or new_cost < cost_so_far[neighbor]):
                cost_so_far[neighbor] = new_cost
                heapq.heappush(frontier, (new_cost, nx, ny))

    return reachable


def flood_fill(start: Position, max_move: int, game_map: Dict[str, Any]) -> Set[Position]:
    """Return set of positions reachable within max_move movement points from start.

    Movement cost is applied when entering a tile. Start position is always included
    (cost 0). Spike tiles are excluded unless they are the only reachable tiles.
    """
    width, height = _get_map_size(game_map)
    sx, sy = start
    if not (0 <= sx < width and 0 <= sy < height):
        return set()

    # Try spike-free first; if nothing reachable beyond start, allow spikes.
    reachable = _flood_fill_core(start, max_move, game_map, width, height, avoid_spikes=True)
    if len(reachable) <= 1:  # only start itself — no safe moves exist
        reachable = _flood_fill_core(start, max_move, game_map, width, height, avoid_spikes=False)
    return reachable


def find_nearest(start, condition: Callable[[Dict[str, Any]], bool], game_map: Dict[str, Any] = None) -> Optional[Position]:
    """Find the nearest position matching a tile predicate using turn-state BFS."""
    game_map = _resolve_map(game_map)
    if game_map is None:
        return None
    start = _pos(start)
    if start is None:
        return None
    width, height = _get_map_size(game_map)
    sx, sy = start
    if not (0 <= sx < width and 0 <= sy < height):
        return None

    root = NearestPickupState(game_map, start, _max_move_of(start), condition)
    path, _, _ = BreadthFirstSearch(game_map).search(root)
    return path[-1].position if path else None


# ──────────────────────────────────────────────────────────────────────────
# Server-accurate movement helpers (mirror PlayerServices.GetReachablePositions)
# ──────────────────────────────────────────────────────────────────────────

def _cell_is_blocked(cell) -> bool:
    """True if a cell holds an occupant the server stops before (entity/item/card)."""
    if not isinstance(cell, dict):
        return False
    return bool(
        cell.get('Entity')      or cell.get('entity')      or
        cell.get('Item')        or cell.get('item')        or
        cell.get('MonsterCard') or cell.get('monsterCard')
    )


def _turn_moves(start, max_move, game_map, width, height, allow_blocked_final=None,
                avoid_spikes=False):
    """All legal end positions reachable in one turn from start.

    The move budget is spent in one straight direction. Each direction can travel
    1..max_move tiles, stopping before walls, empty tiles, and occupied tiles.
    If `allow_blocked_final` is provided, a blocked tile may be yielded as a
    final target when the callback returns True for that cell.
    If `avoid_spikes` is True, spike tiles (Obstacle.Damage > 0) block movement.
    """
    tiles: Set[Position] = set()
    remaining_start = max_move
    if _get_field_type_at(game_map, start[0], start[1], width, height) == OBSTACLE_SLOW:
        remaining_start -= 1

    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        remaining = remaining_start
        x, y = start
        while remaining > 0:
            nx, ny = x + dx, y + dy
            if not (0 <= nx < width and 0 <= ny < height):
                break
            ft = _get_field_type_at(game_map, nx, ny, width, height)
            if ft not in WALKABLE_TYPES:
                break
            cost = 2 if ft == OBSTACLE_SLOW else 1
            if remaining < cost:
                break
            cell = _get_cell_at(game_map, nx, ny, width, height)
            if _cell_is_blocked(cell):
                if allow_blocked_final is not None and allow_blocked_final(cell):
                    tiles.add((nx, ny))
                break
            if avoid_spikes and _has_damage_obstacle(cell):
                break
            tiles.add((nx, ny))
            x, y = nx, ny
            remaining -= cost
    return tiles


class TurnMoveState(SearchState):
    """Turn-based movement state: one node is one position after a turn move."""

    def __init__(self, board, position, max_move, goal=None, parent=None, depth=0,
                 avoid_spikes=False):
        super().__init__(board, parent=parent, depth=depth)
        self.position = position
        self.max_move = max_move
        self.goal = goal
        self.avoid_spikes = avoid_spikes

    def unique_hash(self):
        return self.position

    def is_final_state(self):
        return self.goal is not None and self.position == self.goal

    def get_next_states(self):
        width, height = _get_map_size(self.board)
        next_positions = _turn_moves(self.position, self.max_move, self.board, width, height,
                                     avoid_spikes=self.avoid_spikes)
        return [TurnMoveState(self.board, pos, self.max_move, goal=self.goal,
                              parent=self, depth=self.depth + 1,
                              avoid_spikes=self.avoid_spikes)
                for pos in next_positions]


class NearestPickupState(SearchState):
    """Turn-based state for nearest-tile lookup with a tile predicate."""

    def __init__(self, board, position, max_move, condition, parent=None, depth=0):
        super().__init__(board, parent=parent, depth=depth)
        self.position = position
        self.max_move = max_move
        self.condition = condition

    def unique_hash(self):
        return self.position

    def is_final_state(self):
        tile = _get_cell_at(self.board, self.position[0], self.position[1], *_get_map_size(self.board))
        return tile is not None and self.condition(tile)

    def get_next_states(self):
        width, height = _get_map_size(self.board)

        def allow_blocked(cell):
            return self.condition(cell)

        next_positions = _turn_moves(self.position, self.max_move, self.board, width, height,
                                     allow_blocked_final=allow_blocked)
        return [NearestPickupState(self.board, pos, self.max_move, self.condition,
                                   parent=self, depth=self.depth + 1)
                for pos in next_positions]


def _bfs_path(start, goal, game_map, width, height, avoid_spikes):
    """Full shortest path (list of positions incl. start and goal), or None."""
    frontier = [(0, start[0], start[1])]
    cost_so_far: Dict[Position, int] = {start: 0}
    parent: Dict[Position, Optional[Position]] = {start: None}
    while frontier:
        cost, x, y = heapq.heappop(frontier)
        cur = (x, y)
        if cur == goal:
            path = []
            node = cur
            while node is not None:
                path.append(node)
                node = parent[node]
            path.reverse()
            return path
        for nx, ny in _neighbors(cur, width, height):
            ft = _get_field_type_at(game_map, nx, ny, width, height)
            cell = _get_cell_at(game_map, nx, ny, width, height)
            if not _is_walkable(ft, cell, avoid_spikes):
                continue
            if _cell_is_blocked(cell) and (nx, ny) != goal:
                continue  # server stops before entity/item/card tiles — route around
            new_cost = cost_so_far[cur] + _movement_cost_for(ft)
            nb = (nx, ny)
            if nb not in cost_so_far or new_cost < cost_so_far[nb]:
                cost_so_far[nb] = new_cost
                parent[nb] = cur
                heapq.heappush(frontier, (new_cost, nx, ny))
    return None


def _walk_collect(start, dx, dy, max_move, game_map, width, height, avoid_spikes=False):
    """Every tile reachable in one straight move (dx,dy), mirroring the server.

    The server adds each intermediate tile, costs 2 for ice, stops before
    out-of-bounds / WALL / EMPTY / occupied tiles, and deducts 1 point upfront
    when standing on ice.
    If `avoid_spikes` is True, spike tiles (Obstacle.Damage > 0) also stop movement.
    """
    tiles: Set[Position] = set()
    remaining = max_move
    if _get_field_type_at(game_map, start[0], start[1], width, height) == OBSTACLE_SLOW:
        remaining -= 1
    x, y = start
    while remaining > 0:
        nx, ny = x + dx, y + dy
        if not (0 <= nx < width and 0 <= ny < height):
            break
        ft = _get_field_type_at(game_map, nx, ny, width, height)
        if ft not in WALKABLE_TYPES:
            break
        cost = 2 if ft == OBSTACLE_SLOW else 1
        if remaining < cost:
            break
        cell = _get_cell_at(game_map, nx, ny, width, height)
        if _cell_is_blocked(cell):
            break
        if avoid_spikes and _has_damage_obstacle(cell):
            break
        tiles.add((nx, ny))
        x, y = nx, ny
        remaining -= cost
    return tiles


def _reachable_one_turn(start, max_move, game_map, width, height, avoid_spikes=False):
    out: Set[Position] = set()
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        out |= _walk_collect(start, dx, dy, max_move, game_map, width, height,
                             avoid_spikes=avoid_spikes)
    return out


def find_path(start, goal, game_map: Dict[str, Any] = None,
              avoid_spikes: bool = False) -> Optional[Position]:
    """Best tile to MOVE to this turn, heading toward goal.
    If `avoid_spikes` is True, spike tiles are treated as impassable."""
    return bfs(start, goal, game_map, avoid_spikes=avoid_spikes)


def reachable(start, turns: int = 1, game_map: Dict[str, Any] = None,
              avoid_spikes: bool = False) -> Set[Position]:
    """Set of standable tiles reachable within `turns` turns.
    If `avoid_spikes` is True, spike tiles are excluded from the result."""
    game_map = _resolve_map(game_map)
    if game_map is None:
        return set()
    s = _pos(start)
    if s is None:
        return set()
    width, height = _get_map_size(game_map)
    if not (0 <= s[0] < width and 0 <= s[1] < height):
        return set()
    max_move = _max_move_of(start)

    visited: Set[Position] = {s}
    frontier: Set[Position] = {s}
    for _ in range(max(1, int(turns))):
        next_frontier: Set[Position] = set()
        for tile in frontier:
            next_frontier |= _turn_moves(tile, max_move, game_map, width, height,
                                         avoid_spikes=avoid_spikes)
        next_frontier -= visited
        if not next_frontier:
            break
        visited |= next_frontier
        frontier = next_frontier
    visited.discard(s)
    return visited


def distance(a, b, game_map: Dict[str, Any] = None) -> Optional[int]:
    """Shortest-path movement cost between a and b (ice costs 2), ignoring
    occupants so distance to an enemy/item tile is well-defined. None if
    unreachable. Prefers spike-free routes."""
    game_map = _resolve_map(game_map)
    if game_map is None:
        return None
    s = _pos(a)
    g = _pos(b)
    if s is None or g is None:
        return None
    if s == g:
        return 0
    width, height = _get_map_size(game_map)
    if not (0 <= s[0] < width and 0 <= s[1] < height):
        return None
    if not (0 <= g[0] < width and 0 <= g[1] < height):
        return None
    path = _bfs_path(s, g, game_map, width, height, avoid_spikes=True)
    if path is None:
        path = _bfs_path(s, g, game_map, width, height, avoid_spikes=False)
    if not path:
        return None
    return sum(_movement_cost_for(_get_field_type_at(game_map, x, y, width, height))
               for (x, y) in path[1:])


if __name__ == "__main__":
    W, H = 6, 6  # small map for tests

    def make_map(grid):
        return {'Grid': grid, 'Width': W, 'Height': H}

    def cell(field_type, damage=0):
        c = {'FieldType': field_type}
        if damage:
            c['Obstacle'] = {'Damage': damage, 'Cooldown': 0, 'CurrentCooldown': 0}
        return c

    # ── Test 1: wall blocks, BFS routes around ────────────────────────────────
    grid1 = [cell(NORMAL)] * (W * H)
    grid1[_index(1, 0, W, H)] = cell(OBSTACLE)   # wall at (1,0)
    m1 = make_map(grid1)
    step = bfs((0, 0), (2, 0), m1)
    assert step == (0, 1), f"Test 1 failed: {step}"
    print('Test 1 passed: wall blocks, BFS routes around')

    # ── Test 2: avoid_spikes=True routes around a spike tile ─────────────────
    grid2 = [cell(NORMAL)] * (W * H)
    grid2[_index(1, 0, W, H)] = cell(NORMAL, damage=15)  # spike at (1,0)
    m2 = make_map(grid2)
    step = bfs((0, 0), (2, 0), m2, avoid_spikes=True)
    assert step == (0, 1), f"Test 2 failed: {step}"
    print('Test 2 passed: avoid_spikes=True routes around spike')

    # ── Test 2b: default (avoid_spikes=False) goes straight through spike ────
    step2b = bfs((0, 0), (2, 0), m2)
    assert step2b == (2, 0), f"Test 2b failed: expected (2,0) through spike, got {step2b}"
    print('Test 2b passed: default bfs traverses spike tile directly')

    # ── Test 3: spike is only path — bfs(avoid_spikes=False) reaches goal ────
    grid3 = [cell(NORMAL)] * (W * H)
    # block all routes except through spike at (1,0)
    for y in range(1, H):
        grid3[_index(0, y, W, H)] = cell(WALL)
        grid3[_index(1, y, W, H)] = cell(WALL)
        grid3[_index(2, y, W, H)] = cell(WALL)
    grid3[_index(1, 0, W, H)] = cell(NORMAL, damage=15)  # spike, but only path
    m3 = make_map(grid3)
    step = bfs((0, 0), (2, 0), m3)  # avoid_spikes=False (default)
    # max_move=4: bot reaches (2,0) in one turn (through spike, 1+1=2 budget)
    assert step == (2, 0), f"Test 3 failed: expected (2,0) straight through, got {step}"
    # With avoid_spikes=True the goal is unreachable (no detour exists)
    step3b = bfs((0, 0), (2, 0), m3, avoid_spikes=True)
    assert step3b is None, f"Test 3b failed: expected None, got {step3b}"
    print('Test 3 passed: spike is only path — default reaches it, avoid_spikes=True gives None')

    # ── Test 4: flood_fill excludes spike tiles ───────────────────────────────
    grid4 = [cell(NORMAL)] * (W * H)
    grid4[_index(1, 0, W, H)] = cell(NORMAL, damage=15)  # spike at (1,0)
    m4 = make_map(grid4)
    reached = flood_fill((0, 0), 2, m4)
    assert (1, 0) not in reached, f"Test 4 failed: spike in reachable set"
    print('Test 4 passed: flood_fill excludes spike tiles')

    # ── Test 5: find_nearest skips spike tiles ────────────────────────────────
    grid5 = [cell(NORMAL)] * (W * H)
    grid5[_index(2, 0, W, H)] = cell(POWERUP)             # powerup at (2,0)
    grid5[_index(1, 0, W, H)] = cell(NORMAL, damage=15)   # spike blocks direct route
    m5 = make_map(grid5)
    pos = find_nearest((0, 0), lambda t: isinstance(t, dict) and t.get('FieldType') == POWERUP, m5)
    # Should still find powerup by routing (0,0)→(0,1)→(1,1)→(2,1)→(2,0)
    assert pos == (2, 0), f"Test 5 failed: {pos}"
    print('Test 5 passed: find_nearest reaches powerup around spike')

    # ── Test 6: find_path rides the full move budget in a straight line ───────
    grid6 = [cell(NORMAL)] * (W * H)
    m6 = make_map(grid6)
    dest = find_path((0, 0), (5, 0), m6)   # default budget 4, open eastward row
    assert dest == (4, 0), f"Test 6 failed: expected (4,0), got {dest}"
    print('Test 6 passed: find_path uses full move budget toward goal')

    # ── Test 7: find_path stops before an occupied (item) tile ────────────────
    grid7 = [cell(NORMAL)] * (W * H)
    item_cell = cell(NORMAL)
    item_cell['Item'] = {'Id': 1, 'Name': 'potion', 'ItemType': 1}
    grid7[_index(3, 0, W, H)] = item_cell
    m7 = make_map(grid7)
    dest = find_path((0, 0), (3, 0), m7)   # server stops before the item tile
    assert dest == (2, 0), f"Test 7 failed: expected (2,0), got {dest}"
    print('Test 7 passed: find_path stops adjacent to an item tile')

    # ── Test 8: find_path does not overshoot when the path turns ──────────────
    grid8 = [cell(NORMAL)] * (W * H)
    grid8[_index(1, 0, W, H)] = cell(WALL)   # wall forces a turn at the start
    m8 = make_map(grid8)
    dest = find_path((0, 0), (2, 0), m8)     # path goes down then right; ride only the down leg
    assert dest == (0, 1), f"Test 8 failed: expected (0,1), got {dest}"
    print('Test 8 passed: find_path stops where the optimal path turns')

    # ── Test 9: reachable(turns=1) is the straight-line set, start excluded ────
    grid9 = [cell(NORMAL)] * (W * H)
    m9 = make_map(grid9)
    r = reachable((2, 2), 1, m9)             # default budget 4
    assert (2, 2) not in r, "Test 9 failed: start should be excluded"
    assert (4, 2) in r and (2, 4) in r, f"Test 9 failed: missing straight tiles: {sorted(r)}"
    assert (4, 4) not in r, "Test 9 failed: diagonal tile reachable in one turn"
    print('Test 9 passed: reachable(turns=1) matches straight-line movement')

    # ── Test 10: distance respects ice cost (2) ───────────────────────────────
    grid10 = [cell(NORMAL)] * (W * H)
    grid10[_index(1, 0, W, H)] = cell(OBSTACLE_SLOW)
    grid10[_index(2, 0, W, H)] = cell(OBSTACLE_SLOW)
    m10 = make_map(grid10)
    d = distance((0, 0), (3, 0), m10)        # 1 + 2 + 2 = 5 across two ice tiles
    assert d == 5, f"Test 10 failed: expected 5, got {d}"
    print('Test 10 passed: distance charges 2 per ice tile')

    # ── Test 11: BFS routes AROUND occupied tiles (item/card/entity) ──────────
    grid11 = [cell(NORMAL)] * (W * H)
    item_tile = cell(NORMAL)
    item_tile['Item'] = {'Id': 1, 'Name': 'potion'}
    grid11[_index(2, 2, W, H)] = item_tile        # potion blocks the straight row
    m11 = make_map(grid11)
    path = _bfs_path((0, 2), (4, 2), m11, W, H, avoid_spikes=True)
    assert path and (2, 2) not in path, f"Test 11 failed: routed through item: {path}"
    assert path[0] == (0, 2) and path[-1] == (4, 2), f"Test 11 failed: {path}"
    # find_path must still make progress (not stall before the potion)
    assert find_path((0, 2), (4, 2), m11) is not None, "Test 11 failed: find_path stalled"
    print('Test 11 passed: BFS routes around occupied (item) tiles')

    # ── Test 12: the goal/target tile is still reachable when occupied ────────
    # path TO the item tile is allowed (we stop adjacent in practice)
    path2 = _bfs_path((0, 2), (2, 2), m11, W, H, avoid_spikes=True)
    assert path2 and path2[-1] == (2, 2), f"Test 12a failed: {path2}"
    # find_nearest can still land on the occupied target while avoiding others
    near = find_nearest((0, 2), lambda t: isinstance(t, dict) and t.get('Item') is not None, m11)
    assert near == (2, 2), f"Test 12b failed: {near}"
    print('Test 12 passed: occupied goal/target tiles remain findable')

    print('\nAll tests passed ✓')
