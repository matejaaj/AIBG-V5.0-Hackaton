using System;
using System.Collections.Generic;
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
public class PlayerController : BaseGameController
{
	private readonly IPlayerServices _playerServices;

	private readonly TurnWaitingService _turnWaitingService;

	private readonly IMapServices _mapServices;

	private readonly IAttackService _attackService;

	private readonly ILogger<PlayerController> _logger;

	public PlayerController(IPlayerServices playerServices, TurnWaitingService turnWaitingService, IMapServices mapServices, IAttackService attackService, IGameSessionManager gameSessionManager, ILogger<PlayerController> logger)
		: base(gameSessionManager)
	{
		_playerServices = playerServices;
		_turnWaitingService = turnWaitingService;
		_mapServices = mapServices;
		_attackService = attackService;
		_logger = logger;
	}

	[HttpGet("async/action/{playerId}")]
	public async Task<IActionResult> DoActionAsync([FromRoute] string gameId, [FromRoute] int playerId, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Async action wait requested via /player/async/action/{PlayerId} for gameId={GameId}", playerId, gameId);
		return Ok(await _turnWaitingService.WaitForPlayerActionAsync(gameId, TimeSpan.FromSeconds(30.0), cancellationToken));
	}

	[HttpGet]
	public async Task<Player> Get()
	{
		return new Player();
	}

	[HttpPut("{playerId}/use-item/{itemId}/gameId/{gameId}")]
	public async Task<IActionResult> UseItem([FromRoute] string gameId, [FromRoute] int playerId, [FromRoute] int itemId, CancellationToken cancellationToken)
	{
		GameManager game = _gameSessionManager.GetGameById(gameId);
		Player player = game.GetPlayer(playerId);
		if (!game.TryClaimTurn(player))
		{
			return Conflict("Not Your Turn or Action Already Processing");
		}
		try
		{
			_logger.LogInformation("Use item requested via /player/{PlayerId}/use-item/{ItemId}/gameId/{GameId}", playerId, itemId, gameId);
			bool isBotRequest = _turnWaitingService.IsBotRequest(base.HttpContext);
			_playerServices.UseItem(gameId, playerId, itemId);
			await _turnWaitingService.DelayIfNotUsingUi(base.HttpContext, gameId);
			_turnWaitingService.EndTurnProgrammatically(gameId);
			if (isBotRequest)
			{
				HTTP_ResponseDTO value = new HTTP_ResponseDTO(game.map, gameId, game.GetPlayer(playerId));
				return Ok(value);
			}
			return Ok(await _turnWaitingService.WaitForPlayerActionAsync(gameId, TimeSpan.FromSeconds(30.0), cancellationToken));
		}
		catch (Exception ex)
		{
			Notify(gameId, MessageType.Error, ex.Message);
			return BadRequest(ex.ToString());
		}
	}

	[HttpGet("{id}")]
	public Player Get([FromRoute] string gameId, [FromRoute] int id)
	{
		_logger.LogInformation("Player lookup requested via /player/{GameId}/{PlayerId}", gameId, id);
		return _playerServices.GetPlayer(gameId, id);
	}

	[HttpGet("{playerId}/valid-moves/gameId/{gameId}")]
	public IActionResult GetValidMoves([FromRoute] int playerId, [FromRoute] string gameId)
	{
		try
		{
			_logger.LogInformation("Valid moves requested for player {PlayerId} in game {GameId}", playerId, gameId);
			List<Position> allValidMoves = _playerServices.GetAllValidMoves(gameId, playerId);
			return Ok(allValidMoves);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting valid moves for player {PlayerId}", playerId);
			return BadRequest(ex.Message);
		}
	}

	[HttpPost]
	public Player Post(Player player)
	{
		_logger.LogInformation("Player POST received for player {PlayerName}", player?.Name);
		return player;
	}

	[HttpPut("move/gameId/{gameId}")]
	public async Task<IActionResult> Move([FromRoute] string gameId, [FromBody] MoveRequest moveRequest, CancellationToken cancellationToken)
	{
		GameManager game = _gameSessionManager.GetGameById(gameId);
		Player player = game.GetPlayer(moveRequest.playerId);
		if (!game.TryClaimTurn(player))
		{
			return Conflict("Not Your Turn or Action Already Processing");
		}
		try
		{
			_logger.LogInformation("Move requested via /player/move/gameId/{GameId} for player {PlayerId} to {X},{Y}", gameId, moveRequest.playerId, moveRequest.newPosition?.X, moveRequest.newPosition?.Y);
			bool isBotRequest = _turnWaitingService.IsBotRequest(base.HttpContext);
			await _turnWaitingService.DelayIfNotUsingUi(base.HttpContext, gameId);
			_playerServices.Move(gameId, moveRequest.playerId, moveRequest);
			_logger.LogInformation("Move completed for player {PlayerId} in game {GameId}", moveRequest.playerId, gameId);
			_turnWaitingService.EndTurnProgrammatically(gameId);
			if (isBotRequest)
			{
				HTTP_ResponseDTO value = new HTTP_ResponseDTO(game.map, gameId, game.GetPlayer(moveRequest.playerId));
				return Ok(value);
			}
			return Ok(await _turnWaitingService.WaitForPlayerActionAsync(gameId, TimeSpan.FromSeconds(30.0), cancellationToken));
		}
		catch (Exception ex)
		{
			Notify(gameId, MessageType.Error, ex.Message);
			return BadRequest(ex.ToString());
		}
	}

	[HttpPut("{attackerId}/attack/{attackedId}/gameId/{gameId}")]
	public async Task<IActionResult> Attack([FromRoute] string gameId, [FromRoute] int attackerId, [FromRoute] int attackedId, CancellationToken cancellationToken)
	{
		GameManager game = _gameSessionManager.GetGameById(gameId);
		Player player = game.GetPlayer(attackerId);
		if (!game.TryClaimTurn(player))
		{
			return Conflict("Not Your Turn or Action Already Processing");
		}
		try
		{
			_logger.LogInformation("Attack requested via /player/{AttackerId}/attack/{AttackedId}/gameId/{GameId}", attackerId, attackedId, gameId);
			bool isBotRequest = _turnWaitingService.IsBotRequest(base.HttpContext);
			_attackService.Attack(gameId, attackerId, attackedId);
			await _turnWaitingService.DelayIfNotUsingUi(base.HttpContext, gameId);
			_turnWaitingService.EndTurnProgrammatically(gameId);
			if (isBotRequest)
			{
				HTTP_ResponseDTO value = new HTTP_ResponseDTO(game.map, gameId, game.GetPlayer(attackerId));
				return Ok(value);
			}
			return Ok(await _turnWaitingService.WaitForPlayerActionAsync(gameId, TimeSpan.FromSeconds(30.0), cancellationToken));
		}
		catch (Exception ex)
		{
			Notify(gameId, MessageType.Error, ex.Message);
			return BadRequest(ex.ToString());
		}
	}

	[HttpPut("pickup/{playerId}/gameId/{gameId}")]
	public async Task<IActionResult> PickUpItem([FromRoute] string gameId, [FromBody] FieldInfo fieldInfo, [FromRoute] int playerId, CancellationToken cancellationToken)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		Player player = gameById.GetPlayer(playerId);
		if (!gameById.TryClaimTurn(player))
		{
			return Conflict("Not Your Turn or Action Already Processing");
		}
		try
		{
			_logger.LogInformation("Item pickup requested via /player/pickup/{PlayerId}/gameId/{GameId}", playerId, gameId);
			_playerServices.PickupItem(gameId, player.Id, fieldInfo);
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
