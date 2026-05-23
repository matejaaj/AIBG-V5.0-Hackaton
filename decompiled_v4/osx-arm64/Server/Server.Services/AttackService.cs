using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Server.Services.IServices;
using Server.Utility;
using SharedLibrary;
using SharedLibrary.DTOs;

namespace Server.Services;

public class AttackService : IAttackService
{
	private readonly IPlayerServices _playerService;

	private readonly IMonsterService _monsterService;

	private readonly IGameSessionManager _gameSessionManager;

	private readonly IMonsterAIStrategy _monsterAIStrategy;

	public AttackService(IPlayerServices playerServices, IMonsterService monsterServices, IGameSessionManager gameSessionManager, IMonsterAIStrategy monsterAIStrategy)
	{
		_playerService = playerServices;
		_monsterService = monsterServices;
		_gameSessionManager = gameSessionManager;
		_monsterAIStrategy = monsterAIStrategy;
	}

	public void Attack(string gameId, int attackerId, int attackedId)
	{
		GameManager game = _gameSessionManager.GetGameById(gameId);
		if (game == null)
		{
			throw new Exception("Game not found!");
		}
		Entity attacker = _playerService.GetPlayer(gameId, attackerId) ?? _monsterService.GetMonster(gameId, attackerId);
		if (attacker.xPattern.Length == 0 || attacker.xPattern == null)
		{
			attacker.xPattern = new int[4] { 1, -1, 0, 0 };
		}
		if (attacker.yPattern.Length == 0 || attacker.yPattern == null)
		{
			attacker.yPattern = new int[4] { 0, 0, 1, -1 };
		}
		Entity entity = _playerService.GetPlayer(gameId, attackedId) ?? _monsterService.GetMonster(gameId, attackedId);
		if (attacker == null || entity == null)
		{
			throw new Exception("attacked or attacked are null");
		}
		if (attacker.HasStatus(Status.Fatigue))
		{
			attacker.xPattern = new int[4] { 1, -1, 0, 0 };
			attacker.yPattern = new int[4] { 0, 0, 1, -1 };
			List<int> source = Enumerable.Range(0, attacker.xPattern.Length).Where(delegate(int i)
			{
				int num = attacker.Position.X + attacker.xPattern[i];
				int num2 = attacker.Position.Y + attacker.yPattern[i];
				return num >= 0 && num < game.map.X && num2 >= 0 && num2 < game.map.Y;
			}).ToList();
			if (!source.All(delegate(int i)
			{
				int nx = attacker.Position.X + attacker.xPattern[i];
				int ny = attacker.Position.Y + attacker.yPattern[i];
				return game.map.Grid.Any((FieldInfo f) => f.Entity != null && f.Entity.Id != attacker.Id && f.Position.X == nx && f.Position.Y == ny);
			}))
			{
				throw new Exception("Turn is not possible since the attacker is fatigued");
			}
		}
		if (attacker is Player && entity.SummonedByPlayerId.Equals(attackerId))
		{
			throw new Exception("You cannot attack your own summon");
		}
		for (int j = 1; j <= attacker.AttackRange; j++)
		{
			for (int k = 0; k < attacker.xPattern.Length; k++)
			{
				Position position = new Position(attacker.Position.X + attacker.xPattern[k] * j, attacker.Position.Y + attacker.yPattern[k] * j);
				FieldInfo fieldInfo = game.map.Grid.Find((FieldInfo field) => field.Position.Equals(position));
				if (fieldInfo?.Entity != null && fieldInfo.Entity.Equals(entity))
				{
					attacker.ActiveStatuses[Status.Fatigue] = 2;
					ApplyAttack(game, attacker, entity);
					return;
				}
			}
		}
	}

	public void AttackAOE(string gameId, int attackerId)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Entity entity = _playerService.GetPlayer(gameId, attackerId) ?? _monsterService.GetMonster(gameId, attackerId);
		if (entity.xPattern.Length == 0 || entity.xPattern == null)
		{
			entity.xPattern = new int[4] { 1, -1, 0, 0 };
		}
		if (entity.yPattern.Length == 0 || entity.yPattern == null)
		{
			entity.yPattern = new int[4] { 0, 0, 1, -1 };
		}
		List<Entity> list = new List<Entity>();
		Player player = null;
		for (int i = 1; i <= entity.AttackRange; i++)
		{
			for (int j = 0; j < entity.xPattern.Length; j++)
			{
				Position pos = new Position(entity.Position.X + entity.xPattern[j] * i, entity.Position.Y + entity.yPattern[j] * i);
				FieldInfo fieldAt = gameById.map.GetFieldAt(pos);
				if (fieldAt != null && fieldAt.Entity != null && (!entity.SummonedByPlayerId.HasValue || !(fieldAt.Entity is Player { Id: var id }) || id != entity.SummonedByPlayerId) && !list.Contains(fieldAt.Entity))
				{
					list.Add(fieldAt.Entity);
				}
			}
		}
		List<FieldInfo> list2 = new List<FieldInfo>();
		foreach (Entity item in list)
		{
			item.Health = Math.Max(item.Health - entity.AttackPower, 0);
			if (item is Player player3 && item.Health == 0)
			{
				player = player3;
			}
			else if (item.Health == 0)
			{
				FieldInfo fieldAt2 = gameById.map.GetFieldAt(item.Position);
				fieldAt2.Entity = null;
				list2.Add(fieldAt2);
			}
		}
		AttackDTO value = new AttackDTO(entity, list, entity.AttackPower);
		Notify(gameId, MessageType.Attack, JsonConvert.SerializeObject(value));
		if (player != null)
		{
			gameById.EndGame();
			Player winner = gameById.GetWinner();
			Notify(gameId, MessageType.GameOver, JsonConvert.SerializeObject(winner));
		}
		if (list2.Count > 0)
		{
			Notify(gameId, MessageType.MapUpdate, JsonConvert.SerializeObject(list2));
		}
	}

	public void MonsterAttackOptimal(string gameId, int monsterId)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Entity monster = _monsterService.GetMonster(gameId, monsterId);
		if (monster == null)
		{
			throw new Exception("Monster not found!");
		}
		Entity bestTarget = _monsterAIStrategy.GetBestTarget(gameId, monsterId);
		if (bestTarget != null)
		{
			ApplyAttack(gameById, monster, bestTarget);
			return;
		}
		Position bestMove = _monsterAIStrategy.GetBestMove(gameId, monsterId);
		if (bestMove != null)
		{
			_monsterService.Move(gameId, monsterId, bestMove);
		}
	}

	private static bool IsCardinalTargetInRange(Entity attacker, Entity target)
	{
		bool flag = attacker.Position.Y == target.Position.Y;
		bool flag2 = attacker.Position.X == target.Position.X;
		if (!flag && !flag2)
		{
			return false;
		}
		int num = Math.Abs(attacker.Position.X - target.Position.X) + Math.Abs(attacker.Position.Y - target.Position.Y);
		return num <= attacker.AttackRange;
	}

	private void ApplyAttack(GameManager game, Entity attacker, Entity attacked)
	{
		attacked.Health = Math.Max(attacked.Health - attacker.AttackPower, 0);
		AttackDTO value = new AttackDTO(attacker, attacked, attacker.AttackPower);
		Notify(game.gameId, MessageType.Attack, JsonConvert.SerializeObject(value));
		if (attacked is Player { Health: 0 })
		{
			game.EndGame();
			Player winner = game.GetWinner();
			Notify(game.gameId, MessageType.GameOver, JsonConvert.SerializeObject(winner));
		}
		else if (attacked.Health == 0)
		{
			List<FieldInfo> list = new List<FieldInfo>();
			FieldInfo fieldAt = game.map.GetFieldAt(attacked.Position);
			fieldAt.Entity = null;
			list.Add(fieldAt);
			Notify(game.gameId, MessageType.MapUpdate, JsonConvert.SerializeObject(list));
			if (attacker is Player attacker2)
			{
				BuffPlayers(game, attacker2, attacked);
			}
			else
			{
				BuffPlayers(game, _playerService.GetPlayer(game.gameId, attacker.SummonedByPlayerId.Value), attacked);
			}
		}
	}

	public void BuffPlayers(GameManager game, Player attacker, Entity attacked)
	{
		Player enemyPlayer = game.GetEnemyPlayer(attacker);
		switch (attacked.Name)
		{
		case "Ice cube":
			attacker.AttackPower += 2;
			attacker.Health += 4;
			attacker.MaxHealth += 4;
			enemyPlayer.AttackPower++;
			enemyPlayer.Health += 2;
			enemyPlayer.MaxHealth += 2;
			break;
		case "Ice Mage":
			attacker.AttackPower += 10;
			enemyPlayer.AttackPower += 5;
			break;
		case "Ice Warrior":
			attacker.Health += 10;
			attacker.MaxHealth += 10;
			enemyPlayer.Health += 5;
			enemyPlayer.MaxHealth += 5;
			break;
		default:
			attacker.AttackPower += 2;
			attacker.Health += 4;
			attacker.MaxHealth += 4;
			enemyPlayer.AttackPower++;
			enemyPlayer.Health += 2;
			enemyPlayer.MaxHealth += 2;
			break;
		}
		Notify(game.gameId, MessageType.PlayerUpdate, JsonConvert.SerializeObject(enemyPlayer));
		Notify(game.gameId, MessageType.PlayerUpdate, JsonConvert.SerializeObject(attacker));
	}

	private void Notify(string gameId, MessageType type, string messageData)
	{
		Message message = new Message
		{
			Type = type,
			Data = messageData
		};
		Console.WriteLine(message.ToString());
		WebSocketMangaer.NotifyClients(gameId, message).Wait();
	}
}
