# Game Mechanics (Observed From Decompiled Server v4)

This document describes mechanics as implemented in the decompiled server code, not as intended by UI text.
Primary sources:
- repo/decompiled_v4/osx-arm64/Server/Server.Utility/GameManager.cs
- repo/decompiled_v4/osx-arm64/Server/Server.Utility/TurnWaitingService.cs
- repo/decompiled_v4/osx-arm64/Server/Server.Controller/GameController.cs
- repo/decompiled_v4/osx-arm64/Server/Server.Controller/PlayerController.cs
- repo/decompiled_v4/osx-arm64/Server/Server.Controller/MapController.cs
- repo/decompiled_v4/osx-arm64/Server/Server.Services/PlayerServices.cs
- repo/decompiled_v4/osx-arm64/Server/Server.Services/AttackService.cs
- repo/decompiled_v4/osx-arm64/Server/Server.Services/MonsterService.cs
- repo/decompiled_v4/osx-arm64/Server/Server.Services/GameDataService.cs
- repo/decompiled_v4/osx-arm64/Server/Server.Services/MapServices.cs
- repo/decompiled_v4/osx-arm64/SharedLibrary/SharedLibrary/*.cs
- repo/decompiled_v4/osx-arm64/SharedLibrary/SharedLibrary.DTOs/*.cs

## 1. Core Turn System

### 1.1 Turn states
Turn state enum:
- Start
- Player1Turn
- Player2Turn
- MonsterTurn
- Ending

Transition logic (GameManager.GetNextTurn):
- Start -> Player1Turn
- Player1Turn -> Player2Turn
- Player2Turn -> MonsterTurn
- MonsterTurn -> Player1Turn (unless max turns reached, then Ending)
- Ending -> Ending

### 1.2 Turn counter and max turns
- CurrentTurn starts at 0.
- Turn counter increases only when leaving MonsterTurn (inside IncreaseTurnCounter).
- MaxTurns = 100. When counter reaches MaxTurns, next state becomes Ending.

### 1.3 Start behavior
GameManager.StartGame:
- Sends TurnCounterUpdate with current counter.
- Calls NextTurn.
- Sends TurnChanged with resulting state.

### 1.4 Action lock and conflict behavior
Each game session has an action lock:
- TryClaimTurn(player): fails if either:
  - not player's turn, or
  - IsActionInProgress already true.
- On fail, controllers return HTTP 409 with: "Not Your Turn or Action Already Processing".
- Action lock is released at start of each NextTurn via ReleaseActionFlag.

This is the direct cause of many concurrent action conflicts when clients race requests.

### 1.5 Frozen turn skip
In NextTurn:
- If the new state is Player1Turn/Player2Turn and current player has Frozen, server:
  - emits TurnChanged,
  - immediately recurses to NextTurn again (skip).

So Frozen consumes whole player turns.

### 1.6 Status ticking moment
TickStatuses(player) runs only when NextTurn is entered while current state is Player1Turn or Player2Turn.
That means:
- only players are status-ticked,
- status durations decrease on the affected player's turn entry,
- statuses are not ticked during MonsterTurn.

When a status reaches 0, it is removed and PlayerUpdate is emitted.

### 1.7 Monster phase execution
When NextTurn transitions into MonsterTurn:
- TurnChanged sent.
- Sleeps ~5 ms.
- Collects all non-player entities currently on map, sorted by entity Id.
- For each: AttackService.MonsterAttackOptimal(gameId, monsterId).
- If not No-UI mode, waits ~2 seconds.
- Recurses NextTurn again to continue cycle.

Practical result: monster phase is fully automatic and immediately followed by next player phase.

## 2. HTTP Endpoints and Turn Consumption

### 2.1 Game endpoints
GameController:
- GET /game/start
- GET /game/start/names
- POST /game/start/player-vs-bot
- POST /game/start/bot-vs-bot
- POST /game/connect/{gameId}/{name}
- GET /game/state/{gameId}

State response payload is GameStateDTO:
- GameState (string)
- Players (dictionary int->Player)
- Map
- GameId
- TurnCounter

### 2.2 Player action endpoints
PlayerController actions:
- PUT /player/move/gameId/{gameId}
- PUT /player/{attackerId}/attack/{attackedId}/gameId/{gameId}
- PUT /player/{playerId}/use-item/{itemId}/gameId/{gameId}
- PUT /player/pickup/{playerId}/gameId/{gameId} (item pickup)
- GET /player/{playerId}/valid-moves/gameId/{gameId}
- GET /player/async/action/{playerId}

MapController actions:
- PUT /map/{playerId}/summon/{cardId}/gameId/{gameId}
- PUT /map/pickup/{playerId}/gameId/{gameId} (card pickup)

### 2.3 End-turn behavior from controller actions
For move/attack/use/summon/pickup:
- controller claims turn lock,
- performs action,
- optional delay (2s if request has no No-UI header),
- calls TurnWaitingService.EndTurnProgrammatically(gameId).

EndTurnProgrammatically:
- calls game.NextTurn(),
- if resulting state is Player1Turn or Player2Turn, emits TurnChanged,
- notifies any waiters,
- decreases cooldowns for current player cards.

### 2.4 Bot response vs UI response
If request has No-UI header:
- many endpoints return immediate HTTP_ResponseDTO (map, gameId, player).

Without No-UI:
- endpoints usually block via WaitForPlayerActionAsync (up to 30s) and then return a map/player snapshot.

Note: pickup item endpoint in PlayerController always returns WaitForPlayerActionAsync (no bot short-circuit branch there).

## 3. Movement Mechanics

### 3.1 Reachability model
Movement is generated along four cardinal directions only.
No diagonal movement.

For each direction:
- start with remaining movement = MaxMoveDistance,
- if standing on OBSTACLE_SLOW tile, remaining movement is reduced by 1 immediately,
- step tile-by-tile until blocked.

A step is blocked if next tile:
- out of bounds,
- FieldType is WALL or EMPTY,
- has any Entity,
- has any Item,
- has any MonsterCard,
- or requires more movement than remaining.

Step cost:
- NORMAL/BASE/etc: 1
- OBSTACLE_SLOW: 2

### 3.2 Dizzy effect on movement
If player has Dizzy:
- requested move vector is mirrored to opposite side around current position,
- clamped to map bounds,
- if mirrored target invalid/unreachable, server chooses random reachable position.

So with Dizzy, user intent is distorted and may become random among valid moves.

### 3.3 Trap damage while moving
PlayerServices.FindTrapFields applies obstacle damage on traversed path:
- includes start tile if obstacle present,
- then includes each tile crossed on chosen axis path.

For each trap tile:
- player health reduced by obstacle.Damage, minimum 0,
- if health reaches 0: game ends, GameOver emitted,
- otherwise PlayerUpdate emitted.

After trap processing, actual position change occurs and PlayerMove emitted.

## 4. Attack Mechanics

### 4.1 Targeting and patterns
AttackService.Attack:
- finds attacker as player or monster by id,
- finds target as player or monster by id,
- iterates distance j=1..AttackRange,
- checks attack vectors from attacker xPattern/yPattern.

Default pattern if missing/empty:
- xPattern = [1, -1, 0, 0]
- yPattern = [0, 0, 1, -1]

### 4.2 Own summon protection
If attacker is Player and target.SummonedByPlayerId == attackerId:
- throws error: cannot attack own summon.

### 4.3 Fatigue rule
After a successful Attack call (player attack path), attacker gets:
- Fatigue = 2 turns.

If attacker currently has Fatigue:
- server requires all in-bounds adjacent cardinal tiles to contain some other entity,
- if not all occupied, attack throws: "Turn is not possible since the attacker is fatigued".

Important nuance:
- fatigue check is occupancy-based, not simple cooldown boolean.

### 4.4 Hit resolution
On valid hit:
- target health reduced by attacker.AttackPower (floor at 0),
- Attack message emitted with AttackDTO.

If target is Player and reaches 0:
- game ends,
- GameOver emitted with winner.

If target is non-player and reaches 0:
- target removed from map field,
- MapUpdate emitted,
- then kill buffs applied to players (section 8).

### 4.5 Silent no-hit behavior
If attack target is not found in any legal attack cell:
- AttackService.Attack returns without exception and without attack message.
- Controller still ends turn.

So an invalid-target attack can consume turn without explicit API error.

### 4.6 Monster attack phase behavior
MonsterTurn uses MonsterAttackOptimal per monster:
- if AI finds best target -> ApplyAttack directly,
- else if AI finds best move -> monster moves.

Monster attacks here do not go through player action endpoint turn lock.

## 5. Items

### 5.1 Item types used by gameplay
ItemType enum includes many categories, but current use-item logic handles:
- Healing
- CrowdControl
- Movement

Other types in inventory are rejected as illegal passive use.

### 5.2 Base item definitions (GameDataService)
- Kristal života:
  - Type: Healing
  - Power: 50
  - Uses: 1
  - Effect text: restore health by 50%
  - Implementation: adds +50 health, clamped to MaxHealth.

- Freeze scroll:
  - Type: CrowdControl
  - Duration: 2
  - Uses: 1
  - Applies Frozen to enemy player with value 2.

- Dizzy scroll:
  - Type: Movement
  - Duration: 2
  - Uses: 1
  - Applies Dizzy to enemy player with value 2.

### 5.3 Item use decrement/removal
After successful use:
- Uses decremented if present,
- if uses <= 0: removed from inventory and ItemUsedUp emitted,
- else ItemUsed emitted.

## 6. Pickup Mechanics

### 6.1 Pickup range
Both item and card pickup require target field to be exactly one cardinal-adjacent tile from player.
Player cannot pick from current tile or diagonals.

### 6.2 Item pickup
If adjacent field has item:
- item added to player.Inventory,
- field.Item cleared,
- ItemPicked emitted with player,
- MapUpdate emitted for changed field.

Else emits Error "No item to pick up in the surrounding area.".

### 6.3 Card pickup
If adjacent field has monster card:
- card added to player.Cards,
- field.MonsterCard cleared,
- MonsterCardPicked emitted with player,
- MapUpdate emitted.

Else emits Error "No card to pick up in the surrounding area.".

## 7. Summoning Mechanics

### 7.1 Preconditions
MonsterService.Summon checks:
- player exists,
- card exists in player's hand,
- card not OnCooldown,
- summon field is cardinal-adjacent to player (CanBeSummoned),
- summon field exists on map,
- summon field has no Entity.

If fail: emits Error and throws MonsterCardException.

### 7.2 Placement and ownership
On success:
- card.OnCooldown = true,
- card.CooldownCounter = card.Cooldown,
- monster.SummonedByPlayerId = summoning player id,
- monster gets fresh generated Id,
- monster.Position set to summon field,
- field.Entity = monster,
- card uses decreased,
- MonsterSummoned emitted.

### 7.3 Uses and card removal
DecreaseUses:
- card.Uses = max(0, Uses - 1),
- if uses reaches 0: card removed from hand and CardUsedUp emitted,
- else CardUsed emitted.

### 7.4 Cooldown decrement timing
TurnWaitingService.EndTurnProgrammatically decreases cooldowns for current player after NextTurn.
Practical effect:
- cooldowns tick for the player whose turn has just become active.
- each player's cooldowns generally tick once per full round.

When counter hits 0:
- ResetCooldown -> OnCooldown false, CooldownCounter 0.

## 8. Kill Buff Mechanics

When any non-player entity dies in ApplyAttack:
- buff is awarded to summoner side:
  - if killer is player: that player gets primary buff,
  - if killer is summoned monster: that monster's summoner gets primary buff.

Enemy player gets smaller secondary buff.

Exact buff table (AttackService.BuffPlayers):
- Killed "Ice cube":
  - primary: +2 AttackPower, +4 Health, +4 MaxHealth
  - enemy: +1 AttackPower, +2 Health, +2 MaxHealth

- Killed "Ice Mage":
  - primary: +10 AttackPower
  - enemy: +5 AttackPower

- Killed "Ice Warrior":
  - primary: +10 Health, +10 MaxHealth
  - enemy: +5 Health, +5 MaxHealth

- Any other name (default): same as Ice cube case.

Both players receive PlayerUpdate after buffing.

## 9. Monster Card and Monster Stats (Base Definitions)

GameDataService defines base cards:
- Card 201: Ice cube
  - Uses 3, Cooldown 5
  - Monster: HP 20, MaxHP 30, AP 20, Range 1, Move 3

- Card 202: Ice Mage
  - Uses 2, Cooldown 7
  - Monster: HP 20, MaxHP 20, AP 40, Range 1, Move 2
  - Pattern:
    - xPattern [-3, -3, 2, 2]
    - yPattern [-2, 2, -2, 2]

- Card 203: Ice Warrior
  - Uses 2, Cooldown 7
  - Monster: HP 60, MaxHP 60, AP 25, Range 1, Move 2
  - Pattern:
    - xPattern [0, 0, 1, 2, 0, 0, 1, -1]
    - yPattern [1, 2, 0, 0, -1, -2, 1, 0]

## 10. Map, Obstacles, and Shrink

### 10.1 Field types
Field enum:
- BASE
- NORMAL
- OBSTACLE_SLOW
- OBSTACLE
- POWERUP
- WALL
- EMPTY

### 10.2 Obstacles
Obstacle has:
- Type (Spikes/FallingStone)
- Damage
- Cooldown
- CurrentCooldown

Movement trap damage uses obstacle.Damage directly via FindTrapFields.

### 10.3 Shrink cadence
In IncreaseTurnCounter:
- every 15 turns (if shrinkIndex < 3): ShrinkField
- every 20 turns: regenerate items/cards via GenerateItems

### 10.4 Shrink effect
MapServices.ShrinkMap / SkrinkBridge:
- selected fields set to WALL,
- item/card removed,
- if entity is Player: set health to 0 (entity remains),
- if entity is non-player: remove entity.

GameManager.ShrinkField then checks number of players in shrunk fields:
- 2 -> end game, GameOver null winner
- 1 -> end game, winner by GetWinner
- 0 -> only MapUpdate

Note: GetWinner tie handling for both alive/equal health is inconsistent and may throw instead of returning draw.

## 11. Map Generation and Pickup Respawn

### 11.1 Initial map
Generated from maps.json, then bases set at:
- (3, 7) and (28, 7)

### 11.2 Symmetric placement
Items/cards are placed in mirrored pairs across x axis mirror:
- mirrorX = map.X - 1 - x
- mirrorY = y

Only NORMAL empty fields are used.

### 11.3 Respawn cycles
Every 20 turns, GenerateItems:
- clears all current pickups,
- re-places items/cards based on shrinkIndex profile.

## 12. Messaging and Event Stream

Websocket message envelope:
- Message.Type (MessageType enum)
- Message.Data (JSON string)

Important message types used in mechanics:
- TurnChanged
- TurnCounterUpdate
- Attack
- PlayerMove
- MonsterMove
- PlayerUpdate
- MapUpdate
- ItemPicked
- MonsterCardPicked
- ItemUsed / ItemUsedUp
- CardUsed / CardUsedUp
- MonsterSummoned
- ReloadItems
- Error
- GameOver

## 13. Data Contract Notes

### 13.1 HTTP_ResponseDTO
Returned often for bot-style requests:
- map
- gameId
- player

### 13.2 Entity base fields
Entity includes:
- Id, Name, Health, MaxHealth
- AttackPower, AttackRange, MaxMoveDistance
- Position
- SummonedByPlayerId
- ActiveStatuses (Status -> remaining turns)
- xPattern/yPattern

Player extends Entity with:
- Level, Xp
- Inventory (items)
- Cards (monster cards)
- First (turn side flag)

## 14. Practical Strategy Implications

- A 409 is not only "wrong turn"; it also means action lock was already held.
- Summon legality depends on adjacency + cooldown + card presence + empty entity slot.
- Movement cannot step onto item/card tiles, so pickup usually needs a separate adjacent action.
- Freeze/Dizzy duration value 2 means two affected turn entries in current implementation.
- Invalid attack targeting can still consume turn without explicit error.
- Card cooldown progression is tied to turn transitions, not wall-clock time.
- Monster deaths can buff both sides, with stronger benefit to killer side.
