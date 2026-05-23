using Newtonsoft.Json;
using SharedLibrary.Enums;

namespace SharedLibrary.DTOs;

public class BotVsBotStartRequestDTO
{
	public string Bot1Name { get; set; } = "Bot1";

	public string Bot2Name { get; set; } = "Bot2";

	public string Bot1ScriptPath { get; set; } = "";

	[JsonProperty("bot1ExecutablePath")]
	public string? Bot1ExecutablePath
	{
		get
		{
			return null;
		}
		set
		{
			Bot1ScriptPath = value ?? Bot1ScriptPath;
		}
	}

	public string Bot2ScriptPath { get; set; } = "";

	[JsonProperty("bot2ExecutablePath")]
	public string? Bot2ExecutablePath
	{
		get
		{
			return null;
		}
		set
		{
			Bot2ScriptPath = value ?? Bot2ScriptPath;
		}
	}

	public BotLanguage Bot1Language { get; set; }

	public BotLanguage Bot2Language { get; set; }
}
