# Monster Hunt — Complete Game Documentation

## Overview

Monster Hunt is a **turn-based, 2-player strategy game** played on a 32×16 grid. Each player controls a character, can summon monsters, collect items and cards, and fight the opponent. The goal is to reduce the opponent's HP to zero (or survive longer).

---

## Win Conditions

| Condition | Description |
|-----------|-------------|
| **Kill** | Reduce the opponent's HP to 0 |
| **Outlast** | Have more HP than the opponent after 100 turns |
| **Zone kill** | Force the opponent to stay in the shrinking zone for an instant win |

---

## Turn Order

The turn cycle repeats continuously:

```
Player1Turn → Player2Turn → MonsterTurn → Player1Turn → ...
```

- **Player 1** (`First = true`) acts when `GameState = "Player1Turn"`
- **Player 2** (`First = false`) acts when `GameState = "Player2Turn"`
- **Only 1 action per turn** is allowed

---

## The Map

- **Size**: 32×16 tiles (X: 0–31, Y: 0–15)
- The map **shrinks every 15 turns** — players caught in the shrinking zone are **instantly eliminated**
- New items/power-ups **spawn every N turns**

### Field Types

| Value | Name | Description |
|-------|------|-------------|
| `0` | `BASE` | Base/spawn area |
| `1` | `NORMAL` | Standard walkable tile |
| `2` | `OBSTACLE_SLOW` | Walkable but costs +1 movement (snow) |
| `3` | `OBSTACLE` | Deals damage when stepped on (ice spikes, 10 DMG) |
| `4` | `POWERUP` | Pick up item/card from this tile |
| `5` | `WALL` | Impassable — cannot be entered |
| `6` | `EMPTY` | Non-playable tile |

---

## Player Stats

Each player is an `Entity` with these properties:

| Field | Default | Description |
|-------|---------|-------------|
| `Id` | — | Unique identifier |
| `Name` | — | Player/bot name |
| `Health` | 100 | Current HP |
| `MaxHealth` | 100 | Maximum HP cap |
| `AttackPower` | 10 | Base damage per attack |
| `AttackRange` | 1 | Max tile distance to attack |
| `MaxMoveDistance` | 4 | Max tiles moved per turn |
| `Position` | `{X, Y}` | Current position on the grid |
| `Inventory` | — | Items currently held |
| `Cards` | — | Monster cards in hand |
| `First` | — | `true` = Player 1, `false` = Player 2 |
| `ActiveStatuses` | — | Active status effects with remaining turns |

---

## Actions (1 per turn)

### 1. Move
Move your player to a new position within `MaxMoveDistance` tiles.

```
PUT /player/move/gameId/{gameId}
Body: { "playerId": 1, "newPosition": { "X": 5, "Y": 7 } }
```

Movement rules:
- Player can only move in 4 directions: **left, right, up, down** (no diagonal)
- Each step costs **1 movement point**
- Passing through an `OBSTACLE_SLOW` tile (snow) costs **+1 extra movement point** per such tile
- `WALL`, `OBSTACLE`, and `EMPTY` tiles are impassable
- Cannot move through tiles occupied by other entities
- Total movement budget per turn = `MaxMoveDistance`

---

### 2. Attack
Attack a player or monster by ID.

```
PUT /player/{attackerId}/attack/{attackedId}/gameId/{gameId}
```

- Target must be within `AttackRange` tiles
- Attack hits in a **cross/plus pattern** (left, right, up, down) — same as movement
- Response includes `damage` dealt
- Can target both **enemy players** and **summoned monsters**

---

### 3. Use Item
Use an item from your inventory.

```
PUT /player/{playerId}/use-item/{itemId}/gameId/{gameId}
```

---

### 4. Pick Up Card/Item
Pick up a monster card or item from a `POWERUP` tile.

```
PUT /map/pickup/{playerId}/gameId/{gameId}
Body: { "Position": { "X": 5, "Y": 7 } }
```

- Must be on or adjacent to the tile with the card/item

---

### 5. Summon Monster
Summon a monster from a card you own onto the map.

```
PUT /map/{playerId}/summon/{cardId}/gameId/{gameId}
Body: { "X": 6, "Y": 8 }
```

- Card must not be on cooldown (`OnCooldown = false`)
- Summoned monster acts during `MonsterTurn`

---

## Items (Pomagala)

Items can be found on `POWERUP` tiles or inside your `Inventory`.

### Item Fields

| Field | Description |
|-------|-------------|
| `Id` | Unique ID |
| `Name` | Item name |
| `ItemType` | Category (see below) |
| `Uses` | Remaining uses (nullable) |
| `Effect` | Effect description |
| `Power` | Effect magnitude (nullable) |
| `Range` | Effect range in tiles (nullable) |
| `Duration` | Effect duration in turns (nullable) |

### Items

| Item | Type | Effect |
|------|------|--------|
| Healing Potion | `Healing` | Restores +50 HP |
| Confusion Scroll | `CrowdControl` | Opponent is Dizzy for 2 turns |
| Freeze Scroll | `CrowdControl` | Opponent is Frozen for 2 turns |

---

## Monsters (Čudovišta)

Players can pick up **Monster Cards** from the map and summon monsters.
Summoned monsters act on **MonsterTurn** and fight for their summoner.
Killing enemy monsters grants **buffs** to the summoner.

### Monster Card Fields

| Field | Description |
|-------|-------------|
| `Id` | Card ID |
| `Name` | Monster name |
| `Power` | Power value |
| `OnCooldown` | Is card currently unavailable |
| `Cooldown` | Configured cooldown length in turns |
| `CooldownCounter` | Remaining turns until available |
| `Monster` | The `Entity` spawned on summon |

### Known Monster Types

| Monster | HP | Damage | Movement | Cooldown | Attack Pattern |
|---------|----|--------|----------|----------|----------------|
| Ice Cube | 30 | 20 | 3 tiles | 5 turns | Cross (±1 orthogonal) |
| Ice Golem (small) | 20 | 40 | 2 tiles | 7 turns | Long cross (±2 orthogonal) |
| Ice Golem (large) | 60 | 25 | 2 tiles | 7 turns | 3×3 surrounding area |

---

## Obstacles

Some `OBSTACLE` tiles contain hazards that deal damage when triggered.

| Type | Effect |
|------|--------|
| `Spikes` | 10 damage on step onto the tile + 10 damage when moving away from it |
| `FallingStone` | Variable damage on step |

Each obstacle tracks `CurrentCooldown` / `Cooldown`. The `IsOnCooldown()` method increments `CurrentCooldown` by 1 each time it is called and returns `true` when `CurrentCooldown >= Cooldown` — meaning the obstacle is **ready to deal damage again**. With `Cooldown = 0`, the obstacle triggers on every step.

---

## Status Effects

Status effects are stored in `Entity.ActiveStatuses` as `{ Status: remainingTurns }`.

| Status | Effect |
|--------|--------|
| `Normal` | No effect |
| `Fatigue` | Reduced movement or action capability |
| `Frozen` | Cannot move (Freeze Scroll) |
| `Dizzy` | Impaired actions (Confusion Scroll) |



---

## API — All Endpoints

> **Every PUT endpoint automatically returns the complete updated game state in its response. No separate GET is needed.**

| Method | URL | Purpose |
|--------|-----|---------|
| `GET` | `/game/state/{gameId}` | Get current game state |
| `PUT` | `/player/move/gameId/{gameId}` | Move player |
| `PUT` | `/player/{attackerId}/attack/{attackedId}/gameId/{gameId}` | Attack target |
| `PUT` | `/player/{playerId}/use-item/{itemId}/gameId/{gameId}` | Use inventory item |
| `PUT` | `/map/pickup/{playerId}/gameId/{gameId}` | Pick up card/item from map |
| `PUT` | `/map/{playerId}/summon/{cardId}/gameId/{gameId}` | Summon monster from card |

---

## Game State Structure (JSON)

```json
{
  "GameState": "Player1Turn",
  "Players": {
    "1": {
      "Id": 1, "Name": "MyBot", "First": true,
      "Health": 100, "MaxHealth": 100,
      "AttackPower": 30, "AttackRange": 2, "MaxMoveDistance": 3,
      "Position": { "X": 5, "Y": 7 },
      "Inventory": [...],
      "Cards": [...],
      "ActiveStatuses": {}
    }
  },
  "Monsters": { ... },
  "Map": {
    "Grid": [
      {
        "Position": { "X": 0, "Y": 0 },
        "FieldType": 1,
        "Entity": null,
        "Item": null,
        "MonsterCard": null,
        "Obstacle": null
      },
      ...
    ]
  }
}
```

---

## Quick Reference Card

```
MAP:      32 × 16   (X: 0-31, Y: 0-15)
TURNS:    Player1 → Player2 → Monster → repeat
ACTIONS:  1 per turn: move | attack | use-item | pickup | summon
WIN:      Kill enemy (0 HP) | More HP after 100 turns | Zone trap
ITEMS:    Healing +50HP | Confusion Scroll 2t | Freeze Scroll 2t
OBSTACLES:Snow +1 move cost | Ice Spikes 10 DMG
```
