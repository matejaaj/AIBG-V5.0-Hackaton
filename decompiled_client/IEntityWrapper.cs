using SharedLibrary;

public interface IEntityWrapper
{
	int Id { get; }

	void TriggerAttackAnim();

	void TriggerTakeDamageAnim();

	void EntityUpdate(Entity entity);

	void Move(Position position);
}
