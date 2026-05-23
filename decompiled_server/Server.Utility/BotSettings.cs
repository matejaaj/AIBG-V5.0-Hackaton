namespace Server.Utility;

public class BotSettings
{
	public string PrebuiltBotExecutablePath { get; set; } = "Server/Bots/PrebuiltBot.ps1";

	public string ServerBaseUrl { get; set; } = "http://localhost:8080";
}
