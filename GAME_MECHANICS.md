# Monster Hunt — Mechanics Reference

## Overview
Concise reference for bot implementers: how turns, entities, items, monster cards, movement, obstacles, statuses, and end conditions work. Sources: C# model classes in `Models/` (Entity, Item, ItemType, MonsterCard, Obstacle, FieldInfo, Map, Position, Status), `maps.json`, and `monster-hunt-bot/README.md`. Use the PUT endpoints below — each returns the full game state.

## Turn order
- Cycle: `Player1Turn → Player2Turn → MonsterTurn → Player1Turn`.
- Which player acts is determined by their `First` property:
  - `First = true` → acts on `GameState = "Player1Turn"`.
  - `First = false` → acts on `GameState = "Player2Turn"`.

## HTTP endpoints (exact)
Each listed PUT returns the complete game state in its response.

- Move player:
  - `PUT http://localhost:8080/player/move/gameId/{gameId}`
  - Body example:
    ```json
    {
      "playerId": 1,
      "newPosition": { "X": 5, "Y": 7 }
    }
    ```

- Attack:
  - `PUT http://localhost:8080/player/{attackerId}/attack/{attackedId}/gameId/{gameId}`

- Use item:
  - `PUT http://localhost:8080/player/{playerId}/use-item/{itemId}/gameId/{gameId}`

- Pickup card from map:
  - `PUT http://localhost:8080/map/pickup/{playerId}/gameId/{gameId}`
  - Body example:
    ```json
    {
      "Position": { "X": 5, "Y": 7 }
    }
    ```

- Summon monster from card:
  - `PUT http://localhost:8080/map/{playerId}/summon/{cardId}/gameId/{gameId}`
  - Body example:
    ```json
    {
      "X": 6,
      "Y": 8
    }
    ```

## Entities & stats (fields and meanings)
From `Entity` model:
- `Id` (int) — unique identifier.
- `Name` (string).
- `Health` (int) — current HP.
- `MaxHealth` (int) — HP cap.
- `AttackPower` (int) — base damage when attacking.
- `AttackRange` (int) — distance in tiles allowed to target.
- `MaxMoveDistance` (int) — max tiles entity can move in one move.
- `Position` (`Position` object) — `{ X, Y }`.
- `SummonedByPlayerId` (int?) — player who summoned this entity (if any).
- `ActiveStatuses` (`Dictionary<Status,int>`) — status effects mapped to an int (used as duration/remaining-turns).
- `xPattern`, `yPattern` (int[]) — positional attack/movement pattern arrays.

Implementer note: read `ActiveStatuses` as per-status integer counters (durations/turn counters).

## Items & ItemType (fields and examples)
From `Item` model:
- `Id` (int)
- `Name` (string)
- `Uses` (int?) — remaining uses (nullable).
- `Effect` (string) — description/effect identifier.
- `Range` (int?) — range of item effect (nullable).
- `Power` (int?) — magnitude (nullable).
- `Duration` (int?) — effect duration (nullable).
- `ItemType` (enum `ItemType`) — category.

`ItemType` values (from `ItemType` enum) and examples:
- `None`
- `Healing` — e.g., health crystal (heals).
- `Movement` — e.g., wind boots, teleport stone (movement effects).
- `Defense` — e.g., invisibility cloak (defensive buffs).
- `Utility` — e.g., XP/material gatherers (collecting).
- `CrowdControl` — e.g., freezing powder (slows / disables enemies).
- `Attack` — e.g., damage-boosting items (weapons).

Implementer note: Item fields are nullable where optional; check `Uses` and `Duration` before using.

## Monster cards & cooldowns
From `MonsterCard`:
- `Id`, `Name`, `Uses` (int?), `Effect` (string), `Power` (int).
- `OnCooldown` (bool) — indicates card currently on cooldown / unavailable.
- `Cooldown` (int) — configured cooldown length.
- `CooldownCounter` (int) — current cooldown counter (used to track remaining).
- `Monster` (`Entity`) — the entity spawned when summoning.

Actionable guideline:
- Use `OnCooldown` + `CooldownCounter` to track availability. Typical pattern: when a card is used, set `OnCooldown = true` and set `CooldownCounter = Cooldown`; each turn decrement `CooldownCounter` and when `CooldownCounter <= 0` set `OnCooldown = false`.
- The model itself only stores values; cooldown management occurs at game loop level (implementers must decrement counters on turns).

## Movement & field types
Field types (from README FieldType mapping):
- `0 = BASE`
- `1 = NORMAL` — ordinary walkable tile.
- `2 = OBSTACLE_SLOW` — slows movement (movement cost/steps consumed).
- `3 = OBSTACLE` — obstacle tile (may contain an `Obstacle` object that can deal damage).
- `4 = POWERUP` — pickup tile (pick up card/item).
- `5 = WALL` — impassable.
- `6 = EMPTY` — empty (non-playable) tile.

Implementer guidance:
- `OBSTACLE_SLOW`: treat as walkable but reduce available move distance or require extra move cost per tile.
- `OBSTACLE`: when stepping onto, consult `FieldInfo.Obstacle` for damage/cooldown behavior (see Obstacles section).
- `WALL`: block movement; pathing must avoid.
- `POWERUP`: trigger pickup endpoint (`PUT /map/pickup/...`) or allow immediate pickup action.

Map size: maps in README are 32×16 (X: 0–31, Y: 0–15). `Map.Grid` is a flat list of `FieldInfo` elements; `Map.GetFieldAt(Position)` indexes with `Grid[(pos.X * Y) + pos.Y]` for generated maps.

## Obstacles (damage & cooldown behavior)
From `Obstacle` model:
- Properties:
  - `Type` (`ObstacleType`) — e.g., `Spikes`, `FallingStone`.
  - `Damage` (int) — damage dealt when triggered.
  - `Cooldown` (int) — configured cooldown threshold.
  - `CurrentCooldown` (int) — counter maintained in-game.
- Method semantics:
  - `IsOnCooldown()` increments `CurrentCooldown` by 1 and returns `CurrentCooldown >= Cooldown`.
    - Important: calling `IsOnCooldown()` advances the obstacle's `CurrentCooldown` and then checks whether it has reached the configured `Cooldown`.
    - Implementers: be explicit about when to call this method (e.g., once per turn or on trigger) because it mutates `CurrentCooldown`.

Example (from `maps.json`):
- Example field entry (Position X=0, Y=3):
  ```json
  {
    "Position": { "X": 0, "Y": 3 },
    "FieldType": 3,
    "Obstacle": {
      "Type": 0,
      "Damage": 10,
      "Cooldown": 0,
      "CurrentCooldown": 0
    }
  }
  ```
- Interpretation: an `OBSTACLE` tile with `Damage = 10` and `Cooldown = 0`. With `Cooldown = 0`, `IsOnCooldown()` will increment `CurrentCooldown` and immediately evaluate `CurrentCooldown >= Cooldown` as true on first call — treat such obstacles as always ready unless game code treats `Cooldown=0` specially.

Implementer note: follow the exact `IsOnCooldown()` semantics when modeling obstacle triggers; do not assume non-mutating checks.

## Status effects
From `Status` enum:
- `Normal`
- `Fatigue`
- `Frozen`
- `Dizzy`

Usage:
- `Entity.ActiveStatuses` is a `Dictionary<Status,int>` — the `int` is used as a duration/turn counter for each status (implementer must decrement per turn).
- Use `Entity.HasStatus(Status s)` helper to query presence.

## End conditions
- Explicit GameState: the game loop uses `GameState` values (from README) — `Player1Turn`, `Player2Turn`, `MonsterTurn`. Implementers should also support an explicit `Ending` or equivalent `GameState` to signal the game is over (server-side may set such a state; treat it as terminal).
- Inferred health-based end:
  - Entities have `Health` and `MaxHealth`. A typical inferred end condition is when a player's main entity (or all of a player's entities) reach `Health <= 0`.
  - Implementers should monitor `Entity.Health` in the returned game state to detect player defeat/win and handle /stop playing or submit end-turn actions accordingly.

## Quick integration notes for bots
- Always use the PUT endpoints for actions — each returns the full game state; update your local state from the response (no extra GET required).
- Map access: `Map.Grid` is a flat list of `FieldInfo`. Use `FieldInfo.Position` to map list entries to coordinates or use `Map.GetFieldAt(Position)` for generated maps (note `GetFieldAt` may return null when out-of-bounds).
- Check `FieldInfo.FieldType` integer values (0–6) to decide walkability, pickups, obstacles, and wall blocks.
- Track cooldowns (monster cards, obstacles) explicitly in your bot (server provides counters; apply per-turn decrement logic in your agent to mirror server semantics).
- Respect `ActiveStatuses` durations: decrement per-turn and apply movement/attack restrictions based on status type (`Fatigue`, `Frozen`, `Dizzy`).

--- 
Keep this doc next to your bot code for quick reference to model fields, endpoint usage, and edge-case behaviors (notably `Obstacle.IsOnCooldown()` and `MonsterCard` cooldown counters).
