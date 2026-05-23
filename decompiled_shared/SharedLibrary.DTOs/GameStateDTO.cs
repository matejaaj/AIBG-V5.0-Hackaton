using System.Collections.Generic;

namespace SharedLibrary.DTOs;

public class GameStateDTO
{
	public string GameState { get; set; }

	public Dictionary<int, Player> Players { get; set; }

	public Map Map { get; set; }

	public string GameId { get; set; }

	public int TurnCounter { get; set; }

	public GameStateDTO()
	{
		GameState = "";
		Players = new Dictionary<int, Player>();
		Map = new Map();
		GameId = "";
		TurnCounter = 0;
	}

	public GameStateDTO(TurnState gameState, Dictionary<int, Player> players, Map map, string gameId, int turnCounter)
	{
		GameState = gameState.ToString();
		Players = players;
		Map = map;
		GameId = gameId;
		TurnCounter = turnCounter;
	}
}
