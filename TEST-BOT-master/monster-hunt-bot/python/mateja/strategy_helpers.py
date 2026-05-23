"""Shared strategy helpers used by `final.py`."""
from game_api import is_card, tile_position, _get
from pathfinding import distance


def grab_or_approach(bot, me, world, target, log, within=None):
    """Pick up a target if adjacent, else move toward it."""
    if bot.is_adjacent(me, target):
        log("action: pickup", target)
        tile = world.tile_at(target)
        (bot.pickup_card if is_card(tile) else bot.pickup)(target)
        return True
    log("action: approach pickup", target)
    return bool(bot.move_toward(target, within=within))


def ready_monster_card(card):
    if not isinstance(card, dict):
        return False
    if not _get(card, "Monster", "monster"):
        return False
    if _get(card, "OnCooldown", "onCooldown", default=False):
        return False
    uses = _get(card, "Uses", "uses")
    return uses is None or int(uses) > 0


def summon_monster(bot, me, enemy, center, safe, log, within=None):
    cards = [card for card in (me.cards or []) if ready_monster_card(card)]
    if not cards:
        return False
    world = bot.map
    x, y = me.position
    tiles = []
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        pos = (x + dx, y + dy)
        if world is None or not world.in_bounds(pos):
            continue
        tile = world.tile_at(pos)
        if tile is None or world.get_entity_at(pos) is not None:
            continue
        if within is not None and not within(pos):
            continue
        tiles.append(pos)
    if not tiles:
        return False
    goal = enemy.position if enemy and enemy.position and safe(enemy.position) else center
    pos = min(
        tiles,
        key=lambda p: (
            distance(p, goal) or 1e9,
            abs(p[0] - goal[0]) + abs(p[1] - goal[1]),
            p[0],
            p[1],
        ),
    )
    card = cards[0]
    log("action: summon monster", _get(card, "Name", "name"), pos)
    return bool(bot.summon(card, pos))


def pickup_on_route(me, center, tile):
    """True when a pickup tile lies on a shortest path to center."""
    pickup_pos = tile_position(tile)
    if pickup_pos is None:
        return False
    me_to_pickup = distance(me, pickup_pos)
    pickup_to_center = distance(pickup_pos, center)
    me_to_center = distance(me, center)
    return (
        me_to_pickup is not None
        and pickup_to_center is not None
        and me_to_center is not None
        and me_to_pickup + pickup_to_center == me_to_center
    )
