using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Server.Exceptions;
using Server.Services.IServices;
using Server.Utility;
using SharedLibrary;

namespace Server.Services;

public class PlayerServices : IPlayerServices
{
	private readonly IGameSessionManager _gameSessionManager;

	public Position Position { get; set; } = new Position(0, 0);

	public PlayerServices(IGameSessionManager gameSessionManager)
	{
		_gameSessionManager = gameSessionManager;
	}

	public void Move(string gameId, int playerId, MoveRequest moveRequest)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Player player = GetPlayer(gameId, playerId);
		if (player == null)
		{
			throw new Exception("Player not found!");
		}
		if (player.HasStatus(Status.Dizzy))
		{
			int x = moveRequest.newPosition.X;
			int y = moveRequest.newPosition.Y;
			int x2 = player.Position.X;
			int y2 = player.Position.Y;
			int num = x - x2;
			int num2 = y - y2;
			int num3 = Math.Clamp(x2 - num, 0, gameById.map.X - 1);
			int num4 = Math.Clamp(y2 - num2, 0, gameById.map.Y - 1);
			if (num3 == x2)
			{
				num3 = Math.Clamp(x2 + Math.Sign(num), 0, gameById.map.X - 1);
			}
			if (num4 == y2)
			{
				num4 = Math.Clamp(y2 + Math.Sign(num2), 0, gameById.map.Y - 1);
			}
			moveRequest.newPosition = new Position(num3, num4);
		}
		if (!IsReachablePosition(gameById.map, player, moveRequest.newPosition))
		{
			if (!player.HasStatus(Status.Dizzy))
			{
				throw new Exception("Potez nije moguc");
			}
			List<Position> reachablePositions = GetReachablePositions(gameById.map, player);
			moveRequest.newPosition = reachablePositions[new Random().Next(0, reachablePositions.Count())];
		}
		List<FieldInfo> list = FindTrapFields(gameById.map, player.Position, moveRequest.newPosition);
		foreach (FieldInfo item in list)
		{
			player.Health = Math.Max(player.Health - item.Obstacle.Damage, 0);
			if (player.Health == 0)
			{
				gameById.EndGame();
				Player winner = gameById.GetWinner();
				Notify(gameId, JsonConvert.SerializeObject(winner), MessageType.GameOver);
				return;
			}
			Notify(gameId, JsonConvert.SerializeObject(player), MessageType.PlayerUpdate);
		}
		FieldInfo fieldInfo = gameById.map.Grid.Find((FieldInfo field) => field.Position.Equals(moveRequest.newPosition));
		FieldInfo fieldInfo2 = gameById.map.Grid.Find((FieldInfo field) => field.Position.Equals(player.Position));
		player.Position.X = moveRequest.newPosition.X;
		player.Position.Y = moveRequest.newPosition.Y;
		fieldInfo2.Entity = null;
		fieldInfo.Entity = player;
		Notify(gameId, JsonConvert.SerializeObject(player), MessageType.PlayerMove);
	}

	public List<FieldInfo> FindTrapFields(Map map, Position startPos, Position endPos)
	{
		List<FieldInfo> list = new List<FieldInfo>();
		int num = startPos.X;
		int num2 = startPos.Y;
		int num3 = Math.Sign(endPos.X - startPos.X);
		int num4 = Math.Sign(endPos.Y - startPos.Y);
		FieldInfo fieldAt = map.GetFieldAt(startPos);
		if (fieldAt.Obstacle != null && fieldAt.FieldType == Field.OBSTACLE)
		{
			list.Add(fieldAt);
		}
		if (num3 != 0)
		{
			while (num != endPos.X)
			{
				num += num3;
				fieldAt = map.GetFieldAt(new Position(num, num2));
				if (fieldAt.Obstacle != null)
				{
					list.Add(fieldAt);
				}
			}
		}
		else if (num4 != 0)
		{
			while (num2 != endPos.Y)
			{
				num2 += num4;
				fieldAt = map.GetFieldAt(new Position(num, num2));
				if (fieldAt.Obstacle != null)
				{
					list.Add(fieldAt);
				}
			}
		}
		return list;
	}

	public bool IsReachablePosition(Map map, Player player, Position position)
	{
		return GetReachablePositions(map, player).Find((Position position1) => position1.Equals(position)) != null;
	}

	public List<Position> GetReachablePositions(Map map, Player player)
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
			int num = player.MaxMoveDistance;
			int num2 = player.Position.X;
			int num3 = player.Position.Y;
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
				if (IsPlayerOutOfBound(map, num4, num5))
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

	private void Notify(string gameId, string messageData, MessageType type)
	{
		Message message = new Message
		{
			Type = type,
			Data = messageData
		};
		Console.WriteLine(message.ToString());
		WebSocketMangaer.NotifyClients(gameId, message).Wait();
		Thread.Sleep(5);
	}

	public void UseItem(string gameId, int playerId, int itemId)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Player player = GetPlayer(gameId, playerId);
		if (player == null)
		{
			throw new Exception("Player not found!");
		}
		Item item2 = player.Inventory.FirstOrDefault((Item item) => item.Id == itemId);
		if (item2 == null)
		{
			NotifyError(gameId, "Illegal move tried to use an item that doesn't exist");
			throw new ItemException(itemId, "Illegal move tried to use an item that doesn't exist");
		}
		switch (item2.ItemType)
		{
		case ItemType.Healing:
			player.Health = Math.Min(player.Health + item2.Power.Value, player.MaxHealth);
			NotifyHealedPlayer(gameId, JsonConvert.SerializeObject(player));
			break;
		case ItemType.CrowdControl:
		{
			Player enemyPlayer2 = gameById.GetEnemyPlayer(player);
			enemyPlayer2.ActiveStatuses[Status.Frozen] = item2.Duration.Value;
			NotifyFrozenPlayer(gameId, JsonConvert.SerializeObject(enemyPlayer2));
			break;
		}
		case ItemType.Movement:
		{
			Player enemyPlayer = gameById.GetEnemyPlayer(player);
			enemyPlayer.ActiveStatuses[Status.Dizzy] = item2.Duration.Value;
			NotifyFrozenPlayer(gameId, JsonConvert.SerializeObject(enemyPlayer));
			break;
		}
		default:
			NotifyError(gameId, "Illegal move tried to use a passive item");
			throw new ItemException(itemId, "Illegal move tried to use a passive item");
		}
		if (item2.Uses.HasValue)
		{
			item2.Uses--;
			Console.WriteLine($"Used Item, Remaining uses: {item2.Uses}");
			if (item2.Uses <= 0)
			{
				Console.WriteLine(item2.Name + " is now used up!");
				player.Inventory.Remove(item2);
				NotifyItemUsedUp(gameId, JsonConvert.SerializeObject(item2));
			}
			else
			{
				NotifyUseItem(gameId, JsonConvert.SerializeObject(item2));
			}
		}
	}

	public Player? GetPlayer(string gameId, int id)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		try
		{
			return gameById.GetPlayer(id);
		}
		catch
		{
			return null;
		}
	}

	private void NotifyError(string gameId, string messageData)
	{
		Message message = new Message
		{
			Type = MessageType.Error,
			Data = messageData
		};
		Console.WriteLine(message.ToString());
		WebSocketMangaer.NotifyClients(gameId, message).Wait();
		Thread.Sleep(5);
	}

	private void NotifyUseItem(string gameId, string messageData)
	{
		Message message = new Message
		{
			Type = MessageType.ItemUsed,
			Data = messageData
		};
		WebSocketMangaer.NotifyClients(gameId, message).Wait();
		Thread.Sleep(5);
	}

	private void NotifyItemUsedUp(string gameId, string messageData)
	{
		Message message = new Message
		{
			Type = MessageType.ItemUsedUp,
			Data = messageData
		};
		WebSocketMangaer.NotifyClients(gameId, message).Wait();
		Thread.Sleep(5);
	}

	private void NotifyHealedPlayer(string gameId, string messageData)
	{
		Message message = new Message
		{
			Type = MessageType.PlayerUpdate,
			Data = messageData
		};
		WebSocketMangaer.NotifyClients(gameId, message).Wait();
		Thread.Sleep(5);
	}

	private void NotifyFrozenPlayer(string gameId, string messageData)
	{
		Message message = new Message
		{
			Type = MessageType.PlayerUpdate,
			Data = messageData
		};
		WebSocketMangaer.NotifyClients(gameId, message).Wait();
		Thread.Sleep(5);
	}

	public void PickupItem(string gameId, int playerId, FieldInfo fieldInfo)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Player player = GetPlayer(gameId, playerId);
		if (player == null)
		{
			throw new Exception("Player not found!");
		}
		int[] array = new int[4] { 1, -1, 0, 0 };
		int[] array2 = new int[4] { 0, 0, 1, -1 };
		FieldInfo fieldInfo2 = gameById.map.Grid.Find((FieldInfo info) => info.Position.Equals(fieldInfo.Position));
		for (int i = 0; i < array.Length; i++)
		{
			Position position = new Position(player.Position.X + array[i], player.Position.Y + array2[i]);
			Console.WriteLine(position.ToString());
			if (fieldInfo.Item != null && fieldInfo.Position.Equals(position))
			{
				player.Inventory.Add(fieldInfo.Item);
				fieldInfo2.Item = null;
				Console.WriteLine($"Picked up item: {player.Inventory.Last().Name} from position {position}");
				Notify(gameId, JsonConvert.SerializeObject(player), MessageType.ItemPicked);
				List<FieldInfo> list = new List<FieldInfo>();
				list.Add(fieldInfo2);
				Notify(gameId, JsonConvert.SerializeObject(list), MessageType.MapUpdate);
				return;
			}
		}
		Console.WriteLine("No item to pick up in the surrounding area.");
		NotifyError(gameId, "No item to pick up in the surrounding area.");
	}

	public void PickupCard(string gameId, int playerId, FieldInfo fieldInfo)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Player player = GetPlayer(gameId, playerId);
		if (player == null)
		{
			throw new Exception("Player not found!");
		}
		int[] array = new int[4] { 1, -1, 0, 0 };
		int[] array2 = new int[4] { 0, 0, 1, -1 };
		FieldInfo fieldInfo2 = gameById.map.Grid.Find((FieldInfo info) => info.Position.Equals(fieldInfo.Position));
		for (int i = 0; i < array.Length; i++)
		{
			Position position = new Position(player.Position.X + array[i], player.Position.Y + array2[i]);
			if (fieldInfo.MonsterCard != null && fieldInfo.Position.Equals(position))
			{
				player.Cards.Add(fieldInfo.MonsterCard);
				fieldInfo2.MonsterCard = null;
				Console.WriteLine($"Picked up card: {player.Cards.Last().Name} from position {position}");
				Notify(gameId, JsonConvert.SerializeObject(player), MessageType.MonsterCardPicked);
				List<FieldInfo> list = new List<FieldInfo>();
				list.Add(fieldInfo2);
				Notify(gameId, JsonConvert.SerializeObject(list), MessageType.MapUpdate);
				return;
			}
		}
		Console.WriteLine("No card to pick up in the surrounding area.");
		NotifyError(gameId, "No card to pick up in the surrounding area.");
	}

	private bool IsPlayerOutOfBound(Map map, int x, int y)
	{
		if (x >= 0 && x < map.X && y >= 0)
		{
			return y >= map.Y;
		}
		return true;
	}

	public List<Position> GetAllValidMoves(string gameId, int playerId)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game not found!");
		}
		Player player = GetPlayer(gameId, playerId);
		if (player == null)
		{
			throw new Exception("Player not found!");
		}
		return GetReachablePositions(gameById.map, player);
	}
}
