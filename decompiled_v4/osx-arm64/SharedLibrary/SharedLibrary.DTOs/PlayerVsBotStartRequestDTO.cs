using Newtonsoft.Json;
using SharedLibrary.Enums;

namespace SharedLibrary.DTOs;

public class PlayerVsBotStartRequestDTO
{
	public string PlayerName { get; set; } = "Player";

	public string BotName { get; set; } = "Bot";

	public bool UseInternalAI { get; set; }

	public string BotScriptPath { get; set; } = "";

	[JsonProperty("botExecutablePath")]
	public string? BotExecutablePath
	{
		get
		{
			return null;
		}
		set
		{
			BotScriptPath = value ?? BotScriptPath;
		}
	}

	public BotLanguage BotLanguage { get; set; }
}
