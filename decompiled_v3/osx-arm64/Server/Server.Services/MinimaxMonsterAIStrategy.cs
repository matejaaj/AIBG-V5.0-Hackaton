using System;
using System.Collections.Generic;
using Server.Services.IServices;
using Server.Utility;
using SharedLibrary;

namespace Server.Services;

public class MinimaxMonsterAIStrategy : IMonsterAIStrategy
{
	private readonly IGameSessionManager _gameSessionManager;

	private readonly IMonsterService _monsterService;

	public MinimaxMonsterAIStrategy(IGameSessionManager gameSessionManager, IPlayerServices playerServices, IMonsterService monsterService)
	{
		_gameSessionManager = gameSessionManager;
		_monsterService = monsterService;
	}

	public Entity? GetBestTarget(string gameId, int monsterId)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			return null;
		}
		Entity monster = _monsterService.GetMonster(gameId, monsterId);
		if (monster == null || monster.Health <= 0 || monster.AttackRange <= 0)
		{
			return null;
		}
		Entity result = null;
		int num = int.MinValue;
		List<Entity> targetsInRange = GetTargetsInRange(gameById.map, monster);
		foreach (Entity item in targetsInRange)
		{
			if (item.Health > 0 && !IsAllied(monster, item))
			{
				int num2 = EvaluateTarget(gameId, monster, item, 2);
				if (num2 > num)
				{
					num = num2;
					result = item;
				}
			}
		}
		return result;
	}

	public int EvaluateTarget(string gameId, Entity attacker, Entity target, int depth = 1)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			return 0;
		}
		if (target.Health <= 0)
		{
			return int.MinValue;
		}
		int num = 0;
		int num2 = Math.Max(1, attacker.AttackPower);
		int num3 = target.Health - num2;
		if (num3 <= 0)
		{
			num += 1500;
		}
		else
		{
			num += num2 * 100 / Math.Max(1, target.MaxHealth);
			num += (target.MaxHealth - num3) * 50 / Math.Max(1, target.MaxHealth);
		}
		if (target is Player player)
		{
			int num4 = player.AttackPower * player.AttackRange * 20;
			num += num4;
			if (player.Health < player.MaxHealth / 2)
			{
				num += 200;
			}
		}
		else if (target != null)
		{
			if (target.SummonedByPlayerId.HasValue)
			{
				num += target.AttackPower * 30;
			}
		}
		int num5 = ManhattanDistance(attacker.Position, target.Position);
		if (num5 <= attacker.AttackRange)
		{
			num += (attacker.AttackRange - num5 + 1) * 50;
		}
		if (depth > 0)
		{
			int num6 = EvaluateBestOpponentResponse(gameById.map, attacker, target, num3);
			num -= num6;
		}
		return num;
	}

	private int EvaluateBestOpponentResponse(Map map, Entity attacker, Entity selectedTarget, int selectedTargetRemainingHealth)
	{
		if (selectedTargetRemainingHealth > 0 && !IsAllied(attacker, selectedTarget))
		{
			return EvaluateCounterAttack(selectedTarget, attacker);
		}
		int num = 0;
		foreach (FieldInfo item in map.Grid)
		{
			Entity entity = item.Entity;
			if (entity != null && entity.Health > 0 && !IsAllied(attacker, entity))
			{
				num = Math.Max(num, EvaluateCounterAttack(entity, attacker));
			}
		}
		return num;
	}

	private int EvaluateCounterAttack(Entity opponent, Entity attacker)
	{
		int num = ManhattanDistance(attacker.Position, opponent.Position);
		if (num > opponent.AttackRange)
		{
			return 0;
		}
		int num2 = opponent.AttackPower * 100;
		if (opponent.Health > opponent.MaxHealth / 2)
		{
			num2 += 150;
		}
		return num2;
	}

	private List<Entity> GetTargetsInRange(Map map, Entity attacker)
	{
		List<Entity> list = new List<Entity>();
		HashSet<int> hashSet = new HashSet<int>();
		if (attacker.xPattern.Length == 0 || attacker.xPattern == null)
		{
			attacker.xPattern = new int[4] { 1, -1, 0, 0 };
		}
		if (attacker.yPattern.Length == 0 || attacker.yPattern == null)
		{
			attacker.yPattern = new int[4] { 0, 0, 1, -1 };
		}
		for (int i = 0; i < attacker.xPattern.Length; i++)
		{
			for (int j = 1; j <= attacker.AttackRange; j++)
			{
				int num = attacker.Position.X + attacker.xPattern[i] * j;
				int num2 = attacker.Position.Y + attacker.yPattern[i] * j;
				if (num < 0 || num >= map.X || num2 < 0 || num2 >= map.Y)
				{
					break;
				}
				Position position = new Position(num, num2);
				FieldInfo fieldInfo = map.Grid.Find((FieldInfo tile) => tile.Position != null && tile.Position.Equals(position));
				if (fieldInfo != null && fieldInfo.Entity != null)
				{
					if (fieldInfo.Entity.Id != attacker.Id && hashSet.Add(fieldInfo.Entity.Id))
					{
						list.Add(fieldInfo.Entity);
					}
					break;
				}
			}
		}
		return list;
	}

	private bool IsAllied(Entity attacker, Entity target)
	{
		if (attacker.Id == target.Id)
		{
			return true;
		}
		if (attacker is Player player)
		{
			if (!(target is Player player2) || player2.Id != player.Id)
			{
				return target.SummonedByPlayerId == player.Id;
			}
			return true;
		}
		if (attacker.SummonedByPlayerId.HasValue)
		{
			int value = attacker.SummonedByPlayerId.Value;
			if (target.SummonedByPlayerId != value)
			{
				if (target is Player player3)
				{
					return player3.Id == value;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private int ManhattanDistance(Position pos1, Position pos2)
	{
		return Math.Abs(pos1.X - pos2.X) + Math.Abs(pos1.Y - pos2.Y);
	}

	public Position? GetBestMove(string gameId, int monsterId)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			return null;
		}
		Entity monster = _monsterService.GetMonster(gameId, monsterId);
		if (monster == null || monster.Health <= 0)
		{
			return null;
		}
		List<Position> reachablePositionsForMonster = GetReachablePositionsForMonster(gameById.map, monster);
		if (reachablePositionsForMonster.Count == 0)
		{
			return null;
		}
		Position result = null;
		int num = int.MinValue;
		foreach (Position item in reachablePositionsForMonster)
		{
			int num2 = EvaluateMovePosition(gameById, monster, item);
			if (num2 > num)
			{
				num = num2;
				result = item;
			}
		}
		if (num <= 0)
		{
			return null;
		}
		return result;
	}

	private int EvaluateMovePosition(GameManager game, Entity monster, Position newPosition)
	{
		int num = 0;
		Player player = null;
		int num2 = int.MaxValue;
		foreach (KeyValuePair<int, Player> player2 in game.Players)
		{
			Player value = player2.Value;
			if (value.Health > 0 && !IsAllied(monster, value))
			{
				int num3 = ManhattanDistance(newPosition, value.Position);
				if (num3 < num2)
				{
					num2 = num3;
					player = value;
				}
			}
		}
		if (player != null)
		{
			int num4 = ManhattanDistance(monster.Position, player.Position);
			if (num2 < num4)
			{
				num += (num4 - num2) * 100;
				if (num2 <= monster.AttackRange)
				{
					num += 300;
				}
			}
			else if (num2 > num4)
			{
				num -= (num2 - num4) * 50;
			}
		}
		foreach (KeyValuePair<int, Player> player3 in game.Players)
		{
			Player value2 = player3.Value;
			if (value2.Health > 0 && !IsAllied(monster, value2))
			{
				int num5 = ManhattanDistance(newPosition, value2.Position);
				if (num5 <= value2.AttackRange)
				{
					num -= value2.AttackPower * 30;
				}
			}
		}
		return num;
	}

	private List<Position> GetReachablePositionsForMonster(Map map, Entity monster)
	{
		List<Position> list = new List<Position>();
		Direction[] array = new Direction[4]
		{
			Direction.UP,
			Direction.DOWN,
			Direction.LEFT,
			Direction.RIGHT
		};
		Direction[] array2 = array;
		foreach (Direction direction in array2)
		{
			int num = monster.MaxMoveDistance;
			int num2 = monster.Position.X;
			int num3 = monster.Position.Y;
			if (map.GetFieldAt(new Position(num2, num3)).FieldType == Field.OBSTACLE_SLOW)
			{
				num--;
			}
			while (num > 0)
			{
				int num4 = num2;
				int num5 = num3;
				switch (direction)
				{
				case Direction.UP:
					num5++;
					break;
				case Direction.DOWN:
					num5--;
					break;
				case Direction.LEFT:
					num4--;
					break;
				case Direction.RIGHT:
					num4++;
					break;
				}
				if (num4 < 0 || num4 >= map.X || num5 < 0 || num5 >= map.Y)
				{
					break;
				}
				FieldInfo fieldAt = map.GetFieldAt(new Position(num4, num5));
				if (fieldAt == null || fieldAt.FieldType == Field.WALL || fieldAt.FieldType == Field.EMPTY)
				{
					break;
				}
				int num6 = ((fieldAt.FieldType != Field.OBSTACLE_SLOW) ? 1 : 2);
				if (num < num6 || fieldAt.Entity != null || fieldAt.Item != null || fieldAt.MonsterCard != null)
				{
					break;
				}
				list.Add(fieldAt.Position);
				num2 = num4;
				num3 = num5;
				num -= num6;
			}
		}
		return list;
	}
}
