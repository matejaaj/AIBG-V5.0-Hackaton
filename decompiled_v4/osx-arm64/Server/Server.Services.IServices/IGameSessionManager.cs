using System;
using System.Threading;
using System.Threading.Tasks;
using Server.Utility;
using SharedLibrary;

namespace Server.Services.IServices;

public interface IGameSessionManager
{
	event Action<string> OnGameEnded;

	string CreateNewGame(string player1Name, string player2Name);

	string CreateNewLobby();

	GameManager GetGameById(string gameId);

	void EndGame(string gameId);

	Task<bool> ConnectToGame(string gameId, Player player, TimeSpan timeout, CancellationToken ct);
}
