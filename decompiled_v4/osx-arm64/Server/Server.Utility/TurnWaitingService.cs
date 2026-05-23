using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Server.Services.IServices;
using SharedLibrary;
using SharedLibrary.DAO;
using SharedLibrary.DTOs;

namespace Server.Utility;

public class TurnWaitingService
{
	private IMonsterService _monsterService;

	private IGameSessionManager _gameSessionManager;

	private Dictionary<WaiterClass, TaskCompletionSource<Map>> _waitingStates = new Dictionary<WaiterClass, TaskCompletionSource<Map>>();

	public TurnWaitingService(IMonsterService monsterService, IGameSessionManager gameSessionManager)
	{
		_monsterService = monsterService;
		_gameSessionManager = gameSessionManager;
		_gameSessionManager.OnGameEnded += ResetWaitingTasks;
	}

	public async Task<HTTP_ResponseDTO> WaitForPlayerActionAsync(string gameId, TimeSpan timeout, CancellationToken cancellationToken)
	{
		GameManager game = _gameSessionManager.GetGameById(gameId);
		Player currentPlayer = game.GetCurrentPlayer();
		if (currentPlayer == null)
		{
			return null;
		}
		int waitingPlayerId = currentPlayer.Id;
		TurnState gameState = game.GameState;
		WaiterClass condition = new WaiterClass(gameId, gameState);
		CancellationTokenSource cts1 = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts1.CancelAfter(TimeSpan.FromSeconds(30.0));
		while (!cts1.Token.IsCancellationRequested)
		{
			InsertNewWaitingTask(condition, game);
			Task<Map> task = _waitingStates[condition].Task;
			if (await Task.WhenAny(task, Task.Delay(-1, cts1.Token)) == task)
			{
				_waitingStates.Remove(condition);
				game.GetCurrentPlayer();
				await task;
				return new HTTP_ResponseDTO(game.map, gameId, game.GetPlayer(waitingPlayerId));
			}
		}
		throw new Exception("Other player didn't make a move.");
	}

	private void InsertNewWaitingTask(WaiterClass currentTurn, GameManager game)
	{
		if (!_waitingStates.ContainsKey(currentTurn))
		{
			Console.WriteLine($"Current Turn is {currentTurn.turnState}");
			TaskCompletionSource<Map> value = new TaskCompletionSource<Map>(TaskCreationOptions.RunContinuationsAsynchronously);
			_waitingStates[currentTurn] = value;
		}
	}

	private void NotifyWaitingTasks(WaiterClass condition, GameManager game)
	{
		if (_waitingStates.TryGetValue(condition, out TaskCompletionSource<Map> value))
		{
			value.TrySetResult(game.map);
		}
	}

	public void EndTurnProgrammatically(string gameId)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById != null)
		{
			gameById.NextTurn();
			if (gameById.GameState == TurnState.Player1Turn || gameById.GameState == TurnState.Player2Turn)
			{
				Message message = new Message
				{
					Type = MessageType.TurnChanged,
					Data = JsonConvert.SerializeObject(gameById.GameState)
				};
				WebSocketMangaer.NotifyClients(gameId, message).Wait();
			}
			WaiterClass condition = new WaiterClass(gameId, gameById.GameState);
			NotifyWaitingTasks(condition, gameById);
			Player currentPlayer = gameById.GetCurrentPlayer();
			if (currentPlayer != null)
			{
				_monsterService.DecreaseCooldownsForPlayer(gameId, currentPlayer.Id);
			}
		}
	}

	public void ResetWaitingTasks(string gameId)
	{
		List<WaiterClass> list = _waitingStates.Keys.Where((WaiterClass k) => k.gameId.Equals(gameId)).ToList();
		foreach (WaiterClass item in list)
		{
			if (_waitingStates.TryGetValue(item, out TaskCompletionSource<Map> value))
			{
				value.TrySetCanceled();
			}
			_waitingStates.Remove(item);
		}
	}

	public async Task DelayIfNotUsingUi(HttpContext httpContext, string gameId)
	{
		GameManager game = _gameSessionManager.GetGameById(gameId);
		string value = httpContext.Request.Headers["No-UI"].FirstOrDefault();
		if (string.IsNullOrEmpty(value))
		{
			await Task.Delay(TimeSpan.FromSeconds(2.0));
			game.noUI = false;
		}
		else
		{
			game.noUI = true;
		}
	}

	public bool IsBotRequest(HttpContext httpContext)
	{
		string value = httpContext.Request.Headers["No-UI"].FirstOrDefault();
		return !string.IsNullOrEmpty(value);
	}
}
