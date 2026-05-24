"""Monster Hunt — map API helper classes.

The friendly layer over the raw game-state JSON: enums, tile predicates, and the
Item / Entity / World wrappers your tactics read. Pure data + queries, no I/O.

    from game_api import Field, ItemType, Status, Entity, Item, World
    from game_api import is_pickup, is_healing, is_card, is_powerup
"""
from pathlib import Path
import sys

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from pathfinding import distance, _pos  # noqa: E402


# ── Enums (mirror the C# SharedLibrary) ────────────────────────────────────
class Field:
    BASE = 0
    NORMAL = 1
    OBSTACLE_SLOW = 2   # ice — costs 2 to enter
    OBSTACLE = 3
    POWERUP = 4
    WALL = 5
    EMPTY = 6


WALKABLE = (Field.BASE, Field.NORMAL, Field.OBSTACLE_SLOW, Field.POWERUP)


class ItemType:
    NONE = 0
    HEALING = 1
    MOVEMENT = 2        # inflicts Dizzy on the enemy
    DEFENSE = 3
    UTILITY = 4
    CROWD_CONTROL = 5   # inflicts Frozen on the enemy
    ATTACK = 6
    NAME = {0: 'None', 1: 'Healing', 2: 'Movement', 3: 'Defense',
            4: 'Utility', 5: 'CrowdControl', 6: 'Attack'}


class Status:
    NORMAL = 0
    FATIGUE = 1
    FROZEN = 2
    DIZZY = 3


# ── Tiny dict helpers (the server JSON mixes PascalCase / camelCase) ────────
def _get(d, *keys, default=None):
    if not isinstance(d, dict):
        return default
    for k in keys:
        if k in d and d[k] is not None:
            return d[k]
    return default


def _tile_has_item(tile):
    return _get(tile, 'Item', 'item') is not None


def _tile_has_card(tile):
    return _get(tile, 'MonsterCard', 'monsterCard') is not None


def _tile_has_spike(tile):
    obs = _get(tile, 'Obstacle', 'obstacle')
    if not isinstance(obs, dict):
        return False
    return int(_get(obs, 'Damage', 'damage', default=0) or 0) > 0


def _item_type_of(item):
    if isinstance(item, Item):
        return item.item_type
    return _get(item, 'ItemType', 'itemType')


def _is_item_type(item, type_const, type_name):
    it = _item_type_of(item)
    if it is None:
        return False
    if isinstance(it, bool):
        return False
    if isinstance(it, int):
        return it == type_const
    return str(it).lower() == str(type_name).lower()


# ── Tile predicates (pass these to find_nearest) ───────────────────────────
def is_pickup(tile):
    """Tile holds something you can pick up (item or monster card)."""
    return _tile_has_item(tile) or _tile_has_card(tile)


def is_healing(tile):
    return _is_item_type(_get(tile, 'Item', 'item'), ItemType.HEALING, 'Healing')


def is_card(tile):
    return _tile_has_card(tile)


def is_powerup(tile):
    return _get(tile, 'FieldType', 'fieldType', 'field_type') == Field.POWERUP


def tile_position(tile):
    """(x, y) of a FieldInfo tile, or None."""
    pos = _get(tile, 'Position', 'position') or {}
    x, y = _get(pos, 'X', 'x'), _get(pos, 'Y', 'y')
    return (x, y) if x is not None and y is not None else None


# ── Wrappers ───────────────────────────────────────────────────────────────
class Item:
    def __init__(self, raw):
        self._raw = raw or {}

    @property
    def id(self):
        return _get(self._raw, 'Id', 'id')

    @property
    def name(self):
        return _get(self._raw, 'Name', 'name')

    @property
    def item_type(self):
        return _get(self._raw, 'ItemType', 'itemType')

    @property
    def power(self):
        return _get(self._raw, 'Power', 'power')

    @property
    def duration(self):
        return _get(self._raw, 'Duration', 'duration')

    @property
    def uses(self):
        return _get(self._raw, 'Uses', 'uses')

    @property
    def range(self):
        return _get(self._raw, 'Range', 'range')

    @property
    def raw(self):
        return self._raw

    def __repr__(self):
        return f"Item({self.name!r}, type={self.item_type})"


class Entity:
    """A player or summoned monster from the game state."""

    def __init__(self, raw):
        self._raw = raw or {}

    @property
    def id(self):
        return _get(self._raw, 'Id', 'id')

    @property
    def name(self):
        return _get(self._raw, 'Name', 'name')

    @property
    def health(self):
        return _get(self._raw, 'Health', 'health', default=0)

    @property
    def max_health(self):
        return _get(self._raw, 'MaxHealth', 'maxHealth', default=0)

    @property
    def position(self):
        pos = _get(self._raw, 'Position', 'position') or {}
        return (_get(pos, 'X', 'x'), _get(pos, 'Y', 'y'))

    @property
    def attack_power(self):
        return _get(self._raw, 'AttackPower', 'attackPower', default=0)

    @property
    def attack_range(self):
        return _get(self._raw, 'AttackRange', 'attackRange', default=1)

    @property
    def max_move(self):
        return _get(self._raw, 'MaxMoveDistance', 'maxMoveDistance', default=1)

    @property
    def summoned_by(self):
        return _get(self._raw, 'SummonedByPlayerId', 'summonedByPlayerId')

    @property
    def is_player(self):
        return self.summoned_by is None

    @property
    def statuses(self):
        return _get(self._raw, 'ActiveStatuses', 'activeStatuses') or {}

    def has_status(self, name):
        """name e.g. 'Frozen' / 'Dizzy' / 'Fatigue'."""
        enum_val = str(getattr(Status, name.upper(), None))
        for k, v in self.statuses.items():
            key = str(k).lower()
            if key == name.lower() or key == enum_val:
                try:
                    return int(v) > 0
                except (TypeError, ValueError):
                    return bool(v)
        return False

    @property
    def inventory(self):
        return [Item(i) for i in (_get(self._raw, 'Inventory', 'inventory') or [])]

    @property
    def cards(self):
        return _get(self._raw, 'Cards', 'cards') or []

    @property
    def raw(self):
        return self._raw

    def __repr__(self):
        return f"Entity({self.name!r}, hp={self.health}, pos={self.position})"


class World:
    """Map + entity queries (exposed as bot.map). Positions are (x, y) tuples
    or anything with a .position; tile_at returns the raw FieldInfo dict."""

    def __init__(self, map_raw, my_id, enemy_id, turn_counter):
        self._map = map_raw or {}
        self.my_id = my_id
        self.enemy_id = enemy_id
        self.turn_counter = int(turn_counter or 0)
        self.width = int(_get(self._map, 'X', 'Width', 'width', default=32))
        self.height = int(_get(self._map, 'Y', 'Height', 'height', default=16))
        self._grid = _get(self._map, 'Grid', 'grid') or []
        self._zones = None  # cached shrink-zone union

    @property
    def raw(self):
        return self._map

    def _index(self, x, y):
        return x * self.height + y

    def in_bounds(self, pos):
        x, y = _pos(pos)
        return 0 <= x < self.width and 0 <= y < self.height

    def tile_at(self, pos):
        x, y = _pos(pos)
        if not (0 <= x < self.width and 0 <= y < self.height):
            return None
        i = self._index(x, y)
        return self._grid[i] if 0 <= i < len(self._grid) else None

    def field_type(self, pos):
        tile = self.tile_at(pos)
        return None if tile is None else _get(tile, 'FieldType', 'fieldType', 'field_type')

    def is_walkable(self, pos):
        return self.field_type(pos) in WALKABLE

    def is_wall(self, pos):
        return self.field_type(pos) in (Field.WALL, Field.EMPTY, None)

    def is_powerup(self, pos):
        return self.field_type(pos) == Field.POWERUP

    def is_safe(self, pos):
        """Walkable, not a spike tile, and not in the next shrink zone."""
        if not self.is_walkable(pos):
            return False
        if _tile_has_spike(self.tile_at(pos)):
            return False
        return not self.in_danger_zone(pos)

    def get_entity_at(self, pos):
        tile = self.tile_at(pos)
        ent = _get(tile, 'Entity', 'entity') if tile else None
        return Entity(ent) if ent else None

    def _all_entities(self):
        out = []
        for tile in self._grid:
            if not isinstance(tile, dict):
                continue
            ent_raw = _get(tile, 'Entity', 'entity')
            if not ent_raw:
                continue
            # Entities stored in map tiles often lack a Position field — inject it
            # from the tile so entity.position works correctly for targeting/attack.
            if not _get(ent_raw, 'Position', 'position'):
                tile_pos = _get(tile, 'Position', 'position')
                if tile_pos:
                    ent_raw = {**ent_raw, 'Position': tile_pos}
            out.append(Entity(ent_raw))
        return out

    def _is_mine(self, ent):
        return ent.id == self.my_id or ent.summoned_by == self.my_id

    def enemies(self):
        """Enemy player plus any monsters they summoned."""
        return [e for e in self._all_entities() if not self._is_mine(e)]

    def my_entities(self):
        """Me plus any monsters I summoned."""
        return [e for e in self._all_entities() if self._is_mine(e)]

    def distance(self, a, b):
        return distance(a, b, self._map)

    # ── Map shrink (every 15 turns, edge strips become deadly walls) ───────
    # Cadence is heuristic from TurnCounter — confirm against the live log.
    def shrinks_done(self):
        return min(self.turn_counter // 15, 3)

    def turns_until_shrink(self):
        if self.shrinks_done() >= 3:
            return None
        return 15 - (self.turn_counter % 15)

    def shrink_zone(self, index):
        """Positions that turn to WALL at shrink phase `index` (0,1,2)."""
        zone = set()
        X, Y = self.width, self.height
        if index in (0, 1):
            num = 3
            lo_min, lo_max = index * num, (index + 1) * num
            hi_min, hi_max = X - (index + 1) * num, X - index * num
            for x in range(X):
                if (lo_min <= x < lo_max) or (hi_min <= x < hi_max):
                    for y in range(Y):
                        zone.add((x, y))
        elif index == 2:  # bridge pattern
            for x in range(X):
                if (6 <= x < 13) or (20 <= x < 28):
                    for y in range(Y):
                        if y <= 3 or y >= 12:
                            zone.add((x, y))
        return zone

    def in_danger_zone(self, pos):
        done = self.shrinks_done()
        if done >= 3:
            return False
        return _pos(pos) in self.shrink_zone(done)

    # ── Permanent safe zone (tiles that NEVER become walls) ────────────────
    def shrink_zones_union(self):
        """All positions that become walls over the whole game (cached)."""
        if self._zones is None:
            self._zones = set().union(*(self.shrink_zone(i) for i in (0, 1, 2)))
        return self._zones

    def is_permanently_safe(self, pos):
        """Walkable and never shrunk. Accepts a tuple/entity; None-safe."""
        p = None if (isinstance(pos, tuple) and None in pos) else _pos(pos)
        return p is not None and self.is_walkable(p) and p not in self.shrink_zones_union()

    def safe_zone(self):
        """Every never-shrunk walkable tile."""
        return {(x, y) for x in range(self.width) for y in range(self.height)
                if self.is_permanently_safe((x, y))}

    def center(self):
        """Permanently-safe tile nearest the map middle (the place to hold)."""
        cx, cy = self.width // 2, self.height // 2
        if self.is_permanently_safe((cx, cy)):
            return (cx, cy)
        safe = self.safe_zone()
        return min(safe, key=lambda p: abs(p[0] - cx) + abs(p[1] - cy)) if safe else (cx, cy)
