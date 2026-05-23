using System.Collections.Generic;
using SharedLibrary;

namespace Server.Services;

public interface IGameDataService
{
	Map GetBaseMap();

	Map GetGeneratedMap(int x, int y);

	Player GetBasePlayer(string? name);

	Dictionary<int, Player> SetBasePlayers(string? Player1name, string? Player2name);

	List<FieldInfo> GenerateItems(Map map, int randomItems, int randomCards, int shrinkIndex);

	void setPlayersOnMap(Dictionary<int, Player> players, Map map);
}
