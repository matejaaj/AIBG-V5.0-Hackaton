using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Server.Exceptions;
using Server.Services.IServices;
using Server.Utility;
using SharedLibrary;

namespace Server.Services;

public class MonsterService : IMonsterService
{
	private readonly IGameSessionManager _gameSessionManager;

	public MonsterService(IGameSessionManager gameSessionManager)
	{
		_gameSessionManager = gameSessionManager;
	}

	public Entity? GetMonster(string gameId, int id)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		FieldInfo fieldInfo = gameById.map.Grid.Find((FieldInfo field) => field.Entity != null && id == field.Entity.Id);
		if (fieldInfo != null)
		{
			if (fieldInfo.Position != null)
			{
				fieldInfo.Entity.Position = new Position(fieldInfo.Position.X, fieldInfo.Position.Y);
			}
			return fieldInfo.Entity;
		}
		return null;
	}

	public void ResetCooldown(MonsterCard monster)
	{
		monster.CooldownCounter = 0;
		monster.OnCooldown = false;
	}

	public void DecreaseCooldown(MonsterCard monster)
	{
		monster.CooldownCounter = Math.Max(0, monster.CooldownCounter - 1);
	}

	public void DecreaseCooldownsForPlayer(string gameId, int playerId)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Player player = gameById.GetPlayer(playerId);
		if (player == null)
		{
			throw new Exception("Player not found!");
		}
		foreach (MonsterCard card in player.Cards)
		{
			if (card.OnCooldown)
			{
				DecreaseCooldown(card);
				if (card.CooldownCounter == 0)
				{
					ResetCooldown(card);
				}
				Notify(gameId, MessageType.CardUsed, JsonConvert.SerializeObject(card));
			}
		}
	}

	public bool CanBeSummoned(Player player, Position summonField, GameManager game)
	{
		(int, int)[] array = new(int, int)[4]
		{
			(1, 0),
			(-1, 0),
			(0, 1),
			(0, -1)
		};
		(int, int)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			(int, int) tuple = array2[i];
			Position position = new Position(player.Position.X + tuple.Item1, player.Position.Y + tuple.Item2);
			if (position.Equals(summonField) && game.map.GetFieldAt(position) != null)
			{
				return true;
			}
		}
		return false;
	}

	public void Summon(string gameId, int playerId, int cardId, Position summonField)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Player player = gameById.GetPlayer(playerId);
		if (player == null)
		{
			throw new Exception("Player not found!");
		}
		MonsterCard monsterCard = player.Cards.Find((MonsterCard card) => card.Id == cardId);
		if (monsterCard == null || monsterCard.OnCooldown || !CanBeSummoned(player, summonField, gameById))
		{
			Notify(gameId, MessageType.Error, "Illegal move tried to summon a Monster even though it was on cooldown or didn't exist");
			throw new MonsterCardException(cardId, "Tried to summon a Monster even though it was on cooldown or didn't exist");
		}
		Entity monster = monsterCard.Monster;
		FieldInfo fieldAt = gameById.map.GetFieldAt(summonField);
		if (fieldAt.Entity != null)
		{
			Notify(gameId, MessageType.Error, "Another entity is already on that field");
			throw new MonsterCardException(cardId, "Another entity is already on that field");
		}
		monsterCard.OnCooldown = true;
		monsterCard.CooldownCounter = monsterCard.Cooldown;
		monster.SummonedByPlayerId = player.Id;
		if (fieldAt.Position != null)
		{
			monster.Position = new Position(fieldAt.Position.X, fieldAt.Position.Y);
		}
		monster.Id = IDGenerator.GetNextId();
		fieldAt.Entity = monsterCard.Monster;
		DecreaseUses(gameId, player.Id, monsterCard);
		Notify(gameId, MessageType.MonsterSummoned, JsonConvert.SerializeObject(monster));
	}

	private void DecreaseUses(string gameId, int playerId, MonsterCard card)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Player player = gameById.GetPlayer(playerId);
		if (player == null)
		{
			throw new Exception("Player not found!");
		}
		card.Uses = Math.Max(0, card.Uses.Value - 1);
		Console.WriteLine($"Summoned Monster {card.Monster.Name}, Remaining uses: {card.Uses}");
		if (card.Uses == 0)
		{
			player.Cards.Remove(card);
			Console.WriteLine("{0} is now used up!", card.Name);
			Notify(gameId, MessageType.CardUsedUp, JsonConvert.SerializeObject(card));
		}
		else
		{
			Notify(gameId, MessageType.CardUsed, JsonConvert.SerializeObject(card));
		}
	}

	public void Move(string gameId, int monsterId, Position newPosition)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Entity monster = GetMonster(gameId, monsterId);
		if (monster == null)
		{
			throw new Exception("Monster not found!");
		}
		FieldInfo fieldInfo = gameById.map.Grid.Find((FieldInfo field) => field.Entity != null && field.Entity.Id == monsterId);
		if (fieldInfo == null || fieldInfo.Position == null)
		{
			throw new Exception("Monster current position not found on map");
		}
		if (!IsReachablePosition(startPosition: monster.Position = new Position(fieldInfo.Position.X, fieldInfo.Position.Y), map: gameById.map, maxMoveDistance: monster.MaxMoveDistance, position: newPosition))
		{
			throw new Exception("Monster move not possible");
		}
		FieldInfo fieldInfo2 = gameById.map.Grid.Find((FieldInfo field) => field.Position.Equals(newPosition));
		monster.Position.X = newPosition.X;
		monster.Position.Y = newPosition.Y;
		fieldInfo.Entity = null;
		fieldInfo2.Entity = monster;
		Notify(gameId, MessageType.MonsterMove, JsonConvert.SerializeObject(monster));
	}

	private bool IsReachablePosition(Map map, Position startPosition, int maxMoveDistance, Position position)
	{
		return GetReachablePositions(map, startPosition, maxMoveDistance).Find((Position pos) => pos.Equals(position)) != null;
	}

	private List<Position> GetReachablePositions(Map map, Position startPosition, int maxMoveDistance)
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
			int num = maxMoveDistance;
			int num2 = startPosition.X;
			int num3 = startPosition.Y;
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
