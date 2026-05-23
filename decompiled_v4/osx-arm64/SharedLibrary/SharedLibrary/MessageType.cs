namespace SharedLibrary;

public enum MessageType
{
	PlayerUpdate,
	MonsterUpdate,
	PlayerMove,
	MonsterMove,
	PlayerIndicator,
	Attack,
	MapUpdate,
	ItemUsed,
	HealingItemUsed,
	ItemUsedUp,
	ItemPicked,
	MonsterCardPicked,
	MonsterSummoned,
	CardUsed,
	CardUsedUp,
	TurnChanged,
	SetMaxTurn,
	TurnCounterUpdate,
	Error,
	GameOver,
	ReloadItems
}
