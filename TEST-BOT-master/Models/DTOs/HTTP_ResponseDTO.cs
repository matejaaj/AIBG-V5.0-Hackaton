

namespace SharedLibrary.DTOs;
using Newtonsoft.Json;

public class HTTP_ResponseDTO
{
    public Map map;
    public string gameId;
    public Player player;

    public HTTP_ResponseDTO(Map map, string gameId, Player player)
    {
        this.map = map;
        this.gameId = gameId;
        this.player = player;
    }

    public HTTP_ResponseDTO()
    {
        map = null;
        gameId = "";
        player = null;
    }

}

