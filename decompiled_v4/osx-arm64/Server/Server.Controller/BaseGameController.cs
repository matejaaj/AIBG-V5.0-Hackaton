using System;
using Microsoft.AspNetCore.Mvc;
using Server.Services.IServices;
using Server.Utility;

namespace Server.Controller;

[ApiController]
public abstract class BaseGameController : ControllerBase
{
	protected readonly IGameSessionManager _gameSessionManager;

	protected BaseGameController(IGameSessionManager sessionManager)
	{
		_gameSessionManager = sessionManager;
	}

	protected GameManager GetGame(string gameId)
	{
		GameManager gameById = _gameSessionManager.GetGameById(gameId);
		if (gameById == null)
		{
			throw new Exception("Game session not found.");
		}
		return gameById;
	}
}
