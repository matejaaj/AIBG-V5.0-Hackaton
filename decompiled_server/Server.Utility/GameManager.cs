using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Server.Exceptions;
using Server.Services;
using Server.Services.IServices;
using SharedLibrary;

namespace Server.Utility;

public class GameManager
{
	private readonly IAttackService _attackService;

	private readonly TurnWaitingService _turnWaitingService;

	private readonly IGameDataService _gameDataService;

	private readonly IMapServices _mapServices;

	private readonly object _actionLock = new object();

	public TurnState GameState;

	public int MaxTurns = 100;

	public int TurnCounter;

	public int CurrentTurn;

	public bool noUI;

	public string gameId;

	private int shrinkIndex;

	public TaskCompletionSource<bool> ConnectedPlayers = new TaskCompletionSource<bool>();

	private readonly object _lock = new object();

	public Dictionary<int, Player> Players = new Dictionary<int, Player>();

	public Map map;

	public bool IsActionInProgress { get; private set; }

	public GameManager(string _gameId, IAttackService attackService, IPlayerServices playerServices, IMapServices mapServices, IGameDataService gameDataService, TurnWaitingService turnWaitingService, string? player1Name, string? player2Name)
	{
		_attackService = attackService;
		_turnWaitingService = turnWaitingService;
		_gameDataService = gameDataService;
		_mapServices = mapServices;
		gameId = _gameId;
		Dictionary<int, Player> players = new Dictionary<int, Player>();
		map = _gameDataService.GetGeneratedMap(32, 16);
		if (player1Name != null || player2Name != null)
		{
			players = _gameDataService.SetBasePlayers(player1Name, player2Name);
			_gameDataService.setPlayersOnMap(players, map);
		}
		Players = players;
		GameState = TurnState.Start;
		CurrentTurn = 0;
	}

	public void StartGame()
	{
		Message message = new Message
		{
			Type = MessageType.TurnCounterUpdate,
			Data = JsonConvert.SerializeObject(CurrentTurn)
		};
		WebSocketMangaer.NotifyClients(gameId, message).Wait();
		NextTurn();
		Message message2 = new Message
		{
			Type = MessageType.TurnChanged,
			Data = JsonConvert.SerializeObject(GameState)
		};
		WebSocketMangaer.NotifyClients(gameId, message2).Wait();
	}

	public Player GetPlayer(int Id)
	{
		Player value = Players.FirstOrDefault<KeyValuePair<int, Player>>((KeyValuePair<int, Player> player) => player.Key.Equals(Id)).Value;
		if (value == null)
		{
			throw new Exception("Player doesnt exist");
		}
		return value;
	}

	public void Connect(Player player)
	{
		try
		{
			if (Players.Count >= 2)
			{
				throw new Exception("There are already 2 players connected");
			}
			lock (_lock)
			{
				if (Players.Count == 0)
				{
					player.First = true;
				}
				Players.Add(player.Id, player);
			}
			if (Players.Count == 2)
			{
				_gameDataService.setPlayersOnMap(Players, map);
				StartGame();
				ConnectedPlayers.TrySetResult(result: true);
			}
		}
		catch
		{
			throw new Exception("You are already connected");
		}
	}

	public void ClearConnectedPlayers()
	{
		Players.Clear();
	}

	private bool IncreaseTurnCounter()
	{
		CurrentTurn++;
		if (CurrentTurn % 20 == 0)
		{
			List<FieldInfo> value = _gameDataService.GenerateItems(map, 1, 3);
			Message message = new Message
			{
				Type = MessageType.ReloadItems,
				Data = JsonConvert.SerializeObject(value)
			};
			WebSocketMangaer.NotifyClients(gameId, message).Wait();
		}
		if (CurrentTurn % 15 == 0 && shrinkIndex < 3)
		{
			ShrinkField();
		}
		Message message2 = new Message
		{
			Type = MessageType.TurnCounterUpdate,
			Data = JsonConvert.SerializeObject(CurrentTurn)
		};
		WebSocketMangaer.NotifyClients(gameId, message2).Wait();
		return CurrentTurn < MaxTurns;
	}

	private void ShrinkField()
	{
		List<FieldInfo> list = _mapServices.ShrinkMap(map, shrinkIndex);
		List<FieldInfo> list2 = list.FindAll((FieldInfo info) => info.Entity is Player);
		Message message = new Message();
		switch (list2.Count)
		{
		case 2:
			EndGame();
			message.Type = MessageType.GameOver;
			message.Data = JsonConvert.SerializeObject(null);
			break;
		case 1:
		{
			EndGame();
			Player winner = GetWinner();
			message.Type = MessageType.GameOver;
			message.Data = JsonConvert.SerializeObject(winner);
			break;
		}
		case 0:
			message.Type = MessageType.MapUpdate;
			message.Data = JsonConvert.SerializeObject(list);
			break;
		}
		shrinkIndex++;
		WebSocketMangaer.NotifyClients(gameId, message).Wait();
	}

	public void NextTurn()
	{
		ReleaseActionFlag();
		TurnState gameState = GameState;
		if ((uint)(gameState - 1) <= 1u)
		{
			Player currentPlayer = GetCurrentPlayer();
			if (currentPlayer != null)
			{
				TickStatuses(currentPlayer);
			}
		}
		TurnState gameState2 = GameState;
		TurnState value = (GameState = GetNextTurn());
		Console.WriteLine($"[TURN] Game {gameId}: {gameState2} -> {value}");
		if (GameState != TurnState.MonsterTurn)
		{
			Player currentPlayer2 = GetCurrentPlayer();
			if (currentPlayer2 != null && currentPlayer2.HasStatus(Status.Frozen))
			{
				Console.WriteLine($"[TURN] Player {currentPlayer2.Id} is Frozen, skipping turn");
				Message message = new Message
				{
					Type = MessageType.TurnChanged,
					Data = JsonConvert.SerializeObject(GameState)
				};
				WebSocketMangaer.NotifyClients(gameId, message).Wait();
				NextTurn();
			}
			return;
		}
		Message message2 = new Message
		{
			Type = MessageType.TurnChanged,
			Data = JsonConvert.SerializeObject(GameState)
		};
		WebSocketMangaer.NotifyClients(gameId, message2);
		Thread.Sleep(5);
		List<int> list = (from info in map.Grid.FindAll((FieldInfo info) => info.Entity != null && !(info.Entity is Player))
			orderby info.Entity.Id
			select info.Entity.Id).ToList();
		try
		{
			foreach (int item in list)
			{
				_attackService.MonsterAttackOptimal(gameId, item);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.ToString());
		}
		if (!noUI)
		{
			Task.Delay(TimeSpan.FromSeconds(2.0)).Wait();
		}
		NextTurn();
	}

	public void TickStatuses(Player player)
	{
		TurnState gameState = GameState;
		if ((uint)(gameState - 1) > 1u)
		{
			return;
		}
		bool flag = false;
		if (player.ActiveStatuses.Count <= 0)
		{
			return;
		}
		List<Status> list = new List<Status>();
		foreach (Status item in player.ActiveStatuses.Keys.ToList())
		{
			player.ActiveStatuses[item]--;
			if (player.ActiveStatuses[item] <= 0)
			{
				list.Add(item);
			}
		}
		foreach (Status item2 in list)
		{
			player.ActiveStatuses.Remove(item2);
			flag = true;
			Console.WriteLine($"Player's {player.Id} status {item2} expired");
		}
		if (flag)
		{
			Message message = new Message
			{
				Type = MessageType.PlayerUpdate,
				Data = JsonConvert.SerializeObject(player)
			};
			WebSocketMangaer.NotifyClients(gameId, message).Wait();
		}
	}

	public TurnState GetNextTurn()
	{
		switch (GameState)
		{
		case TurnState.Start:
			return TurnState.Player1Turn;
		case TurnState.Player1Turn:
			return TurnState.Player2Turn;
		case TurnState.Player2Turn:
			return TurnState.MonsterTurn;
		case TurnState.MonsterTurn:
			if (!IncreaseTurnCounter())
			{
				return TurnState.Ending;
			}
			return TurnState.Player1Turn;
		case TurnState.Ending:
			return TurnState.Ending;
		default:
			return TurnState.Player1Turn;
		}
	}

	public void EndGame()
	{
		GameState = TurnState.Ending;
	}

	public Player GetWinner()
	{
		Player[] array = Players.Values.ToArray();
		if (array[0].Health > 0 && array[1].Health > 0)
		{
			if (array[0].Health > array[1].Health)
			{
				return array[0];
			}
			if (array[1].Health > array[0].Health)
			{
				return array[1];
			}
			if (array[1] == array[0])
			{
				return null;
			}
		}
		if (array[0].Health == 0)
		{
			return array[1];
		}
		if (array[1].Health == 0)
		{
			return array[0];
		}
		throw new Exception("Something went wrong");
	}

	public bool IsMyTurn(Player player)
	{
		return GameState switch
		{
			TurnState.Player1Turn => player.First, 
			TurnState.Player2Turn => !player.First, 
			_ => throw new InvalidPlayerException(player.Id, "Not your turn"), 
		};
	}

	public Player GetCurrentPlayer()
	{
		return GameState switch
		{
			TurnState.Player1Turn => Players.Values.FirstOrDefault((Player player) => player.First), 
			TurnState.Player2Turn => Players.Values.FirstOrDefault((Player player) => !player.First), 
			_ => null, 
		};
	}

	public Player GetEnemyPlayer(Player player)
	{
		return GameState switch
		{
			TurnState.Player1Turn => Players.Values.First((Player playerVal) => !playerVal.First), 
			TurnState.Player2Turn => Players.Values.First((Player playerVal) => playerVal.First), 
			TurnState.MonsterTurn => Players.Values.First((Player playerVal) => playerVal.First != player.First), 
			_ => null, 
		};
	}

	public FieldInfo GetBaseField()
	{
		if (GetCurrentPlayer().First)
		{
			return map.Grid.Find((FieldInfo field) => field.Position.Equals(new Position(0, 0)));
		}
		return map.Grid.Find((FieldInfo field) => field.Position.Equals(new Position(map.X - 1, map.Y - 1)));
	}

	public bool TryClaimTurn(Player player)
	{
		lock (_actionLock)
		{
			if (!IsMyTurn(player) || IsActionInProgress)
			{
				return false;
			}
			IsActionInProgress = true;
			return true;
		}
	}

	public void ReleaseActionFlag()
	{
		lock (_actionLock)
		{
			IsActionInProgress = false;
		}
	}
}
