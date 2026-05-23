namespace SharedLibrary.DTOs;
using Newtonsoft.Json;

public class GameEssentialsDTO
{
    public Map map;
    public string gameId;

    public GameEssentialsDTO()
    {
        map = null;
        gameId = "";
    }
    public GameEssentialsDTO(Map map,string gameId)
    {
        this.map = map;
        this.gameId = gameId;
    }
}