using System.Collections.Generic;
using SharedLibrary;

namespace Server.Services.IServices;

public interface IPlayerServices
{
	void Move(string gameId, int playerId, MoveRequest moveRequest);

	Player? GetPlayer(string gameId, int id);

	void UseItem(string gameId, int playerId, int itemId);

	void PickupItem(string gameId, int playerId, FieldInfo fieldInfo);

	void PickupCard(string gameId, int playerId, FieldInfo fieldInfo);

	List<Position> GetAllValidMoves(string gameId, int playerId);
}
