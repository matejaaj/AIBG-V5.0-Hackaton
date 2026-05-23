using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Server.Services.IServices;
using Server.Utility;
using SharedLibrary;

namespace Server.Services;

public class GameSessionManager : IGameSessionManager
{
	private readonly ConcurrentDictionary<string, GameManager> _activeGames = new ConcurrentDictionary<string, GameManager>();

	private readonly IGameManagerFactory _gameManagerFactory;

	public event Action<string> OnGameEnded;

	public GameSessionManager(IGameManagerFactory gameManagerFactory)
	{
		_gameManagerFactory = gameManagerFactory;
	}

	public string CreateNewGame(string player1Name, string player2Name)
	{
		string text = Guid.NewGuid().ToString();
		GameManager value = _gameManagerFactory.Create(text, player1Name, player2Name);
		_activeGames.TryAdd(text, value);
		return text;
	}

	public string CreateNewLobby()
	{
		string text = Guid.NewGuid().ToString();
		GameManager value = _gameManagerFactory.Create(text, null, null);
		_activeGames.TryAdd(text, value);
		return text;
	}

	public GameManager GetGameById(string gameId)
	{
		_activeGames.TryGetValue(gameId, out GameManager value);
		return value;
	}

	public void EndGame(string gameId)
	{
		_activeGames.TryRemove(gameId, out GameManager value);
		value.EndGame();
		this.OnGameEnded?.Invoke(gameId);
	}

	public async Task<bool> ConnectToGame(string gameId, Player player, TimeSpan timeout, CancellationToken ct)
	{
		GameManager game = GetGameById(gameId);
		if (game == null)
		{
			game = _gameManagerFactory.Create(gameId, null, null);
			_activeGames.TryAdd(gameId, game);
		}
		game.Connect(player);
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cancellationTokenSource.CancelAfter(timeout);
		Task<bool> task = game.ConnectedPlayers.Task;
		if (await Task.WhenAny(task, Task.Delay(-1, cancellationTokenSource.Token)) == task)
		{
			return true;
		}
		game.ClearConnectedPlayers();
		return false;
	}
}
