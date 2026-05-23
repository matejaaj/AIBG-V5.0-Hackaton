using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Server.Services;
using Server.Services.IServices;
using Server.Utility;
using SharedLibrary;

namespace Server.Controller;

[ApiController]
[Route("[controller]")]
public class MapController : BaseGameController
{
	private readonly IMapServices _mapServices;

	private readonly IPlayerServices _playerServices;

	private readonly IMonsterService _monsterService;

	private readonly TurnWaitingService _turnWaitingService;

	private readonly ILogger<MapController> _logger;

	public MapController(IMapServices mapServices, IMonsterService monsterService, TurnWaitingService turnWaitingService, IPlayerServices playerServices, IGameSessionManager sessionManager, ILogger<MapController> logger)
		: base(sessionManager)
	{
		_mapServices = mapServices;
		_monsterService = monsterService;
		_turnWaitingService = turnWaitingService;
		_playerServices = playerServices;
		_logger = logger;
	}

	[HttpGet("{id}")]
	public string Get([FromRoute] int id)
	{
		try
		{
			_logger.LogInformation("Map requested via /map/{MapId}", id);
			Map map = _mapServices.GetMap(id);
			return JsonConvert.SerializeObject(map);
		}
		catch (Exception ex)
		{
			throw new Exception(ex.Message);
		}
	}

	[HttpPut("{playerId}/summon/{cardId}/gameId/{gameId}")]
	public async Task<IActionResult> Summon([FromRoute] string gameId, [FromRoute] int playerId, [FromRoute] int cardId, [FromBody] Position summonField, CancellationToken cancellationToken)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		Player player = gameById.GetPlayer(playerId);
		if (!gameById.TryClaimTurn(player))
		{
			return Conflict("Not Your Turn or Action Already Processing");
		}
		try
		{
			_logger.LogInformation("Summon requested via /map/{GameId}/summon by player {PlayerId} for card {CardId}", gameId, playerId, cardId);
			_monsterService.Summon(gameId, playerId, cardId, summonField);
			await _turnWaitingService.DelayIfNotUsingUi(base.HttpContext, gameId);
			_turnWaitingService.EndTurnProgrammatically(gameId);
			return Ok(await _turnWaitingService.WaitForPlayerActionAsync(gameId, TimeSpan.FromSeconds(30.0), cancellationToken));
		}
		catch (Exception ex)
		{
			Notify(gameId, MessageType.Error, ex.Message);
			return BadRequest(ex.ToString());
		}
	}

	[HttpPut("pickup/{playerId}/gameId/{gameId}")]
	public async Task<IActionResult> PickUpCard([FromRoute] int playerId, [FromRoute] string gameId, [FromBody] FieldInfo fieldInfo, CancellationToken cancellationToken)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		Player player = gameById.GetPlayer(playerId);
		if (!gameById.TryClaimTurn(player))
		{
			return Conflict("Not Your Turn or Action Already Processing");
		}
		try
		{
			_logger.LogInformation("Map pickup requested via /map/pickup/{PlayerId}/gameId/{GameId}", playerId, gameId);
			_playerServices.PickupCard(gameId, playerId, fieldInfo);
			_turnWaitingService.EndTurnProgrammatically(gameId);
			return Ok(await _turnWaitingService.WaitForPlayerActionAsync(gameId, TimeSpan.FromSeconds(30.0), cancellationToken));
		}
		catch (Exception ex)
		{
			Notify(gameId, MessageType.Error, ex.Message);
			return BadRequest(ex.ToString());
		}
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
