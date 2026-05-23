using SharedLibrary;

namespace Server.Services.IServices;

public interface IMonsterService
{
	Entity? GetMonster(string gameId, int id);

	void ResetCooldown(MonsterCard monster);

	void DecreaseCooldown(MonsterCard monster);

	void Summon(string gameId, int playerId, int cardId, Position summonField);

	void DecreaseCooldownsForPlayer(string gameId, int playerId);

	void Move(string gameId, int monsterId, Position newPosition);
}
