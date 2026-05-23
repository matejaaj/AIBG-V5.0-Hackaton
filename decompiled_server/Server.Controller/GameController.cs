using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Server.Services;
using Server.Services.IServices;
using Server.Utility;
using SharedLibrary;
using SharedLibrary.DTOs;

namespace Server.Controller;

[ApiController]
[Route("[controller]")]
public class GameController : BaseGameController
{
	private readonly IBotProcessService _botProcessService;

	private readonly BotSettings _botSettings;

	private readonly ILogger<GameController> _logger;

	private readonly BotAIService _botAIService;

	private readonly Dictionary<string, List<int>> _gameBotProcesses = new Dictionary<string, List<int>>();

	public GameController(IGameSessionManager sessionManager, IBotProcessService botProcessService, BotSettings botSettings, ILogger<GameController> logger, BotAIService botAIService)
		: base(sessionManager)
	{
		_botProcessService = botProcessService;
		_botSettings = botSettings;
		_logger = logger;
		_botAIService = botAIService;
		_gameSessionManager.OnGameEnded += CleanupBotProcesses;
	}

	private async void CleanupBotProcesses(string gameId)
	{
		if (!_gameBotProcesses.TryGetValue(gameId, out List<int> value))
		{
			return;
		}
		_logger.LogInformation("Cleaning up {Count} bot processes for game {GameId}", value.Count, gameId);
		foreach (int processId in value)
		{
			await _botProcessService.StopBotProcessAsync(processId);
			_logger.LogInformation("Stopped bot process {ProcessId} for game {GameId}", processId, gameId);
		}
		_gameBotProcesses.Remove(gameId);
	}

	[HttpGet("start")]
	public async Task<IActionResult> StartGame([FromQuery] int? player1Id, [FromQuery] int? player2Id)
	{
		_logger.LogInformation("Game start requested via /game/start with player1Id={Player1Id}, player2Id={Player2Id}", player1Id, player2Id);
		return Ok(_gameSessionManager.CreateNewLobby());
	}

	[HttpGet("start/names")]
	public async Task<IActionResult> StartGameWithNames([FromQuery] string player1Name, [FromQuery] string player2Name)
	{
		_logger.LogInformation("PvP start requested via /game/start/names with player1Name={Player1Name}, player2Name={Player2Name}", player1Name, player2Name);
		string gameId = _gameSessionManager.CreateNewGame(player1Name, player2Name);
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		GameEssentialsDTO value = new GameEssentialsDTO(gameById.map, gameId);
		return Ok(value);
	}

	[HttpPost("start/player-vs-bot")]
	public async Task<IActionResult> StartPlayerVsBot([FromBody] PlayerVsBotStartRequestDTO request)
	{
		try
		{
			_logger.LogInformation("PvB start requested via /game/start/player-vs-bot with playerName={PlayerName}, botName={BotName}, botLanguage={BotLanguage}", request?.PlayerName, request?.BotName, request?.BotLanguage);
			if (request == null || string.IsNullOrWhiteSpace(request.PlayerName))
			{
				return BadRequest("playerName is required");
			}
			if (string.IsNullOrWhiteSpace(request.BotName))
			{
				return BadRequest("botName is required");
			}
			if (string.IsNullOrWhiteSpace(request.BotScriptPath))
			{
				return BadRequest("botScriptPath is required");
			}
			string gameId = _gameSessionManager.CreateNewGame(request.PlayerName, request.BotName);
			GameManager game = _gameSessionManager.GetGameById(gameId);
			Player player = game.Players.Values.FirstOrDefault((Player p) => p.Name == request.BotName);
			if (player == null)
			{
				_logger.LogError("Failed to find pre-created bot player in game {GameId}", gameId);
				return StatusCode(500, "Failed to create bot player");
			}
			_logger.LogInformation("Created game {GameId} for player vs bot, will start when WebSocket connects", gameId);
			string serverBaseUrl = $"{base.Request.Scheme}://{base.Request.Host}";
			int num = await _botProcessService.StartBotProcessAsync(request.BotScriptPath, request.BotLanguage, serverBaseUrl, gameId, request.BotName, player.Id);
			if (num == -1)
			{
				_logger.LogError("Failed to start bot process for game {GameId}", gameId);
				return StatusCode(500, "Failed to start bot process");
			}
			if (!_gameBotProcesses.ContainsKey(gameId))
			{
				_gameBotProcesses[gameId] = new List<int>();
			}
			_gameBotProcesses[gameId].Add(num);
			_logger.LogInformation($"Started bot process for game {gameId}: Bot PID={num}");
			GameEssentialsDTO value = new GameEssentialsDTO(game.map, gameId);
			return Ok(value);
		}
		catch (Exception ex)
		{
			_logger.LogError("Error in StartPlayerVsBot: " + ex.Message);
			return StatusCode(500, "An error occurred starting the game");
		}
	}

	[HttpPost("start/bot-vs-bot")]
	public async Task<IActionResult> StartBotVsBot([FromBody] BotVsBotStartRequestDTO request)
	{
		try
		{
			_logger.LogInformation("BvB start requested via /game/start/bot-vs-bot with bot1={Bot1Name} ({Bot1Lang}), bot2={Bot2Name} ({Bot2Lang})", request?.Bot1Name, request?.Bot1Language, request?.Bot2Name, request?.Bot2Language);
			if (request == null || string.IsNullOrWhiteSpace(request.Bot1Name) || string.IsNullOrWhiteSpace(request.Bot2Name) || string.IsNullOrWhiteSpace(request.Bot1ScriptPath) || string.IsNullOrWhiteSpace(request.Bot2ScriptPath))
			{
				return BadRequest("Bot names and script paths are required");
			}
			string gameId = _gameSessionManager.CreateNewGame(request.Bot1Name, request.Bot2Name);
			GameManager game = _gameSessionManager.GetGameById(gameId);
			Player player = game.Players.Values.FirstOrDefault((Player p) => p.Name == request.Bot1Name);
			Player bot2Player = game.Players.Values.FirstOrDefault((Player p) => p.Name == request.Bot2Name);
			if (player == null || bot2Player == null)
			{
				_logger.LogError("Failed to find pre-created bot players in game {GameId}", gameId);
				return StatusCode(500, "Failed to create bot players");
			}
			_logger.LogInformation("Created game {GameId} for bot vs bot, will start when WebSocket connects", gameId);
			string serverBaseUrl = $"{base.Request.Scheme}://{base.Request.Host}";
			int bot1ProcessId = await _botProcessService.StartBotProcessAsync(request.Bot1ScriptPath, request.Bot1Language, serverBaseUrl, gameId, request.Bot1Name, player.Id);
			int bot2ProcessId = await _botProcessService.StartBotProcessAsync(request.Bot2ScriptPath, request.Bot2Language, serverBaseUrl, gameId, request.Bot2Name, bot2Player.Id);
			if (bot1ProcessId == -1 || bot2ProcessId == -1)
			{
				_logger.LogError("Failed to start one or both bot processes for game {GameId}", gameId);
				if (bot1ProcessId != -1)
				{
					await _botProcessService.StopBotProcessAsync(bot1ProcessId);
				}
				if (bot2ProcessId != -1)
				{
					await _botProcessService.StopBotProcessAsync(bot2ProcessId);
				}
				return StatusCode(500, "Failed to start bot processes");
			}
			_gameBotProcesses[gameId] = new List<int> { bot1ProcessId, bot2ProcessId };
			_logger.LogInformation($"Started bot processes for game {gameId}: Bot1 PID={bot1ProcessId}, Bot2 PID={bot2ProcessId}");
			GameEssentialsDTO value = new GameEssentialsDTO(game.map, gameId);
			return Ok(value);
		}
		catch (Exception ex)
		{
			_logger.LogError("Error in StartBotVsBot: " + ex.Message);
			return StatusCode(500, "An error occurred starting the bot vs bot game");
		}
	}

	[HttpPost("connect/{gameId}/{name}")]
	public async Task<IActionResult> Connect([FromRoute] string gameId, [FromRoute] string name)
	{
		try
		{
			_logger.LogInformation("Player connect request to /game/connect/{GameId}/{Name}", gameId, name);
			Player player = new Player
			{
				Name = name,
				Id = IDGenerator.GetNextId()
			};
			bool flag = await _gameSessionManager.ConnectToGame(gameId, player, TimeSpan.FromSeconds(30.0), default(CancellationToken));
			Map map = _gameSessionManager.GetGameById(gameId).map;
			HTTP_ResponseDTO value = new HTTP_ResponseDTO(map, gameId, player);
			return flag ? StatusCode(200, value) : StatusCode(501, "Other Player failed to load");
		}
		catch (Exception ex)
		{
			string message = ex.Message;
			return (message == "There are already 2 players connected") ? BadRequest("There are already 2 players connected") : ((!(message == "You are already connected")) ? StatusCode(500, "An unexpected error occurred") : BadRequest("You are already connected"));
		}
	}

	[HttpGet("state/{gameId}")]
	public IActionResult GetGameState([FromRoute] string gameId)
	{
		try
		{
			_logger.LogInformation("Game state requested via /game/state/{GameId}", gameId);
			GameManager gameById = _gameSessionManager.GetGameById(gameId);
			if (gameById == null)
			{
				return NotFound("Game " + gameId + " not found");
			}
			GameStateDTO value = new GameStateDTO(gameById.GameState, gameById.Players, gameById.map, gameId, gameById.CurrentTurn);
			return Ok(value);
		}
		catch (Exception ex)
		{
			_logger.LogError("Error getting game state for {GameId}: {Error}", gameId, ex.Message);
			return StatusCode(500, "An error occurred retrieving game state");
		}
	}
}
