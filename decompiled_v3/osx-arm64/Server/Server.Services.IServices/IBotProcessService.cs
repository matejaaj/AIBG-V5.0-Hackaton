using System.Threading.Tasks;
using SharedLibrary.Enums;

namespace Server.Services.IServices;

public interface IBotProcessService
{
	Task<int> StartBotProcessAsync(string scriptPath, BotLanguage language, string serverBaseUrl, string gameId, string playerName, int? playerId = null);

	Task<bool> StopBotProcessAsync(int processId);

	bool IsProcessRunning(int processId);

	Task StopAllProcessesAsync();
}
