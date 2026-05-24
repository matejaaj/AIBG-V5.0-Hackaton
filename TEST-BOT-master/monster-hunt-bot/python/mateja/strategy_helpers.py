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
    monster = _get(card, "Monster", "monster")
    if not monster:
        return False
    # Monster is already alive on the map — cannot summon a second instance.
    # SummonedByPlayerId is set when the monster is placed, cleared when it dies.
    if _get(monster, "SummonedByPlayerId", "summonedByPlayerId") is not None:
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


def can_afford_pickup(me, center, tile, turns_until_shrink, max_move):
    """True if we can reach tile AND then center before the next map shrink.

    Uses two-phase turn estimation:
      leg 1 — current position → pickup  (ceil(dist / max_move) turns)
      leg 2 — pickup position  → center  (ceil(dist / max_move) turns, fresh start)
    Each leg is ceilinged separately because each turn can only move in one
    straight direction, so the direction reset between legs is a real cost.

    One additional turn is kept as a safety margin.
    If turns_until_shrink is None (all shrinks done) there is no time pressure.
    """
    pickup_pos = tile_position(tile)
    if pickup_pos is None:
        return False
    me_to_pickup     = distance(me, pickup_pos)
    pickup_to_center = distance(pickup_pos, center)
    if me_to_pickup is None or pickup_to_center is None:
        return False
    if turns_until_shrink is None:
        return True     # no more shrinks — grab anything reachable
    turns_leg1 = (me_to_pickup     + max_move - 1) // max_move
    turns_leg2 = (pickup_to_center + max_move - 1) // max_move
    return turns_leg1 + turns_leg2 <= turns_until_shrink - 1  # -1 safety margin
