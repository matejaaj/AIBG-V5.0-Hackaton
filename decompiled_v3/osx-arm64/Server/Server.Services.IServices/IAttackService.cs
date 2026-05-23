namespace Server.Services.IServices;

public interface IAttackService
{
	void Attack(string gameId, int attackerId, int attackedId);

	void AttackAOE(string gameId, int attackerId);

	void MonsterAttackOptimal(string gameId, int monsterId);
}
