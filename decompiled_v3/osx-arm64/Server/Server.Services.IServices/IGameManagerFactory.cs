using Server.Utility;

namespace Server.Services.IServices;

public interface IGameManagerFactory
{
	GameManager Create(string Id, string? player1Name, string? player2Name);
}
