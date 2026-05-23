namespace SharedLibrary.DAO;

public record WaiterClass
{
	public string gameId;

	public TurnState turnState;

	public WaiterClass(string _gameId, TurnState _turnState)
	{
		gameId = _gameId;
		turnState = _turnState;
	}
}
