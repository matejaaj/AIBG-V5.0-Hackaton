"""Offline smoke test for the bot API — no server required.

Builds a fake 32x16 game state and checks find_path / reachable / find_nearest /
distance / is_safe / can_attack / enemies() behave sensibly. Run: python smoke_test_api.py
"""
from game_client import Bot
from game_api import Entity, Field, ItemType, is_pickup
from pathfinding import find_path, reachable, find_nearest, distance

W, H = 32, 16


def make_state(me_pos, enemy_pos, item_pos):
    grid = []
    for x in range(W):
        for y in range(H):           # index = x*H + y (matches server)
            grid.append({'Position': {'X': x, 'Y': y}, 'FieldType': Field.NORMAL,
                         'Entity': None, 'Item': None, 'MonsterCard': None, 'Obstacle': None})

    def cell(pos):
        return grid[pos[0] * H + pos[1]]

    me_entity = {'Id': 1, 'Name': 'Me', 'Health': 50, 'MaxHealth': 100,
                 'Position': {'X': me_pos[0], 'Y': me_pos[1]},
                 'AttackPower': 10, 'AttackRange': 3, 'MaxMoveDistance': 4,
                 'SummonedByPlayerId': None, 'First': True, 'ActiveStatuses': {},
                 'Inventory': [{'Id': 99, 'Name': 'Kristal zivota', 'ItemType': ItemType.HEALING, 'Power': 30, 'Uses': 1}],
                 'Cards': []}
    enemy_entity = {'Id': 2, 'Name': 'Foe', 'Health': 80, 'MaxHealth': 100,
                    'Position': {'X': enemy_pos[0], 'Y': enemy_pos[1]},
                    'AttackPower': 10, 'AttackRange': 1, 'MaxMoveDistance': 4,
                    'SummonedByPlayerId': None, 'First': False, 'ActiveStatuses': {},
                    'Inventory': [], 'Cards': []}

    cell(me_pos)['Entity'] = me_entity
    cell(enemy_pos)['Entity'] = enemy_entity
    cell(item_pos)['Item'] = {'Id': 7, 'Name': 'Kristal zivota', 'ItemType': ItemType.HEALING, 'Power': 30}

    return {
        'GameState': 'Player1Turn',
        'TurnCounter': 0,
        'Map': {'X': W, 'Y': H, 'Name': 'snow', 'Grid': grid},
        'Players': {'1': me_entity, '2': enemy_entity},
    }


def main():
    me_pos, enemy_pos, item_pos = (3, 8), (10, 8), (3, 11)
    state = make_state(me_pos, enemy_pos, item_pos)

    bot = Bot('http://localhost:8080', 'game', 'Me')
    assert bot.find_my_player_id(state), "could not find player id"
    bot.update(state)
    me, enemy = bot.me, bot.enemy

    assert me.position == (3, 8), me.position
    assert enemy.position == (10, 8), enemy.position
    assert me.max_move == 4 and me.attack_range == 3
    print("ok: state parsed (me, enemy, map)")

    dest = find_path(me, enemy)            # ride east, full budget 4
    assert dest == (7, 8), f"find_path -> {dest}"
    print(f"ok: find_path uses full budget -> {dest}")

    r = reachable(me, turns=1)
    assert (7, 8) in r, "near tile should be reachable"
    assert me.position not in r, "start excluded"
    assert enemy.position not in r, "occupied enemy tile excluded"
    print(f"ok: reachable(turns=1) has {len(r)} tiles, excludes start/enemy")

    near = find_nearest(me, is_pickup)
    assert near == (3, 11), f"find_nearest(is_pickup) -> {near}"
    print(f"ok: find_nearest(is_pickup) -> {near}")

    assert distance(me, enemy) == 7, distance(me, enemy)
    print("ok: distance(me, enemy) == 7")

    assert bot.map.is_safe((3, 8)) is True, "interior tile should be safe"
    assert bot.map.is_safe((1, 8)) is False, "edge tile in shrink zone 0 should be unsafe"
    assert bot.map.turns_until_shrink() == 15
    print("ok: is_safe + shrink-zone awareness")

    # enemy is to the RIGHT and far -> not attackable (range + server right-skip)
    assert bot.can_attack(enemy) is False
    # a foe to the LEFT within range is attackable
    left_foe = Entity({'Id': 3, 'Position': {'X': 1, 'Y': 8}})
    assert bot.can_attack(left_foe) is True
    print("ok: can_attack respects range and the right-direction quirk")

    enemies = bot.map.enemies()
    assert any(e.position == (10, 8) for e in enemies), "enemy not found on grid"
    assert all(e.id != 1 for e in enemies), "own entity leaked into enemies()"
    print(f"ok: enemies() -> {[e.position for e in enemies]}")

    assert bot.healing_item() is not None and bot.has_item(ItemType.HEALING)
    print("ok: inventory lookup (healing item)")

    # ── permanent safe zone (never-shrunk region) ─────────────────────────
    w = bot.map
    assert w.center() == (16, 8), w.center()
    assert w.is_permanently_safe((16, 8)) is True       # center
    assert w.is_permanently_safe((19, 15)) is True      # spine, full height
    assert w.is_permanently_safe((8, 8)) is True        # left wing, mid rows
    assert w.is_permanently_safe((8, 0)) is False       # wing top — shrunk by bridge
    assert w.is_permanently_safe((1, 8)) is False       # outer column — shrunk
    assert w.is_permanently_safe((26, 8)) is False      # fully walled column
    assert len(w.safe_zone()) == 216, len(w.safe_zone())
    assert w.is_permanently_safe(None) is False
    print(f"ok: safe zone — center {w.center()}, {len(w.safe_zone())} tiles")

    # ── move_toward within= guard refuses leaving a region ────────────────
    # me is at (3,8); with a guard that rejects every tile, no move is sent.
    assert bot.move_toward((0, 8), within=lambda p: False) is None
    print("ok: move_toward(within=...) refuses to leave the region")

    print("\nAll smoke tests passed ✓")


if __name__ == "__main__":
    main()
