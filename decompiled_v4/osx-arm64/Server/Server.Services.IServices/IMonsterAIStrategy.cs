using SharedLibrary;

namespace Server.Services.IServices;

public interface IMonsterAIStrategy
{
	Entity? GetBestTarget(string gameId, int monsterId);

	int EvaluateTarget(string gameId, Entity attacker, Entity target, int depth = 1);

	Position? GetBestMove(string gameId, int monsterId);
}
