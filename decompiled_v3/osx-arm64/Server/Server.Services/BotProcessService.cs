using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Server.Services.IServices;
using SharedLibrary.Enums;

namespace Server.Services;

public class BotProcessService : IBotProcessService
{
	private readonly ILogger<BotProcessService> _logger;

	private readonly Dictionary<int, Process> _activeProcesses = new Dictionary<int, Process>();

	public BotProcessService(ILogger<BotProcessService> logger)
	{
		_logger = logger;
	}

	public async Task<int> StartBotProcessAsync(string scriptPath, BotLanguage language, string serverBaseUrl, string gameId, string playerName, int? playerId = null)
	{
		try
		{
			if (!File.Exists(scriptPath))
			{
				_logger.LogError("Bot script not found: " + scriptPath);
				return -1;
			}
			ProcessStartInfo processStartInfo = ConfigureProcessForLanguage(language, scriptPath, serverBaseUrl, gameId, playerName, playerId);
			if (processStartInfo == null)
			{
				_logger.LogError($"Failed to configure process for language: {language}");
				return -1;
			}
			Process process = new Process
			{
				StartInfo = processStartInfo
			};
			process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
			{
				if (!string.IsNullOrEmpty(e.Data))
				{
					_logger.LogInformation("Bot " + playerName + " output: " + e.Data);
				}
			};
			process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
			{
				if (!string.IsNullOrEmpty(e.Data))
				{
					_logger.LogError("Bot " + playerName + " error: " + e.Data);
				}
			};
			if (!process.Start())
			{
				_logger.LogError("Failed to start bot process: " + scriptPath);
				return -1;
			}
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			_activeProcesses[process.Id] = process;
			_logger.LogInformation($"Bot process started with ID {process.Id} for {playerName} using {language}: {scriptPath}");
			return process.Id;
		}
		catch (Exception ex)
		{
			_logger.LogError("Error starting bot process: " + ex.Message);
			return -1;
		}
	}

	private ProcessStartInfo? ConfigureProcessForLanguage(BotLanguage language, string scriptPath, string serverBaseUrl, string gameId, string playerName, int? playerId = null)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		string text = (playerId.HasValue ? $"{serverBaseUrl} {gameId} \"{playerName}\" {playerId.Value}" : $"{serverBaseUrl} {gameId} \"{playerName}\"");
		switch (language)
		{
		case BotLanguage.Python:
			processStartInfo.FileName = (OperatingSystem.IsWindows() ? "python" : "python3");
			processStartInfo.Arguments = "-u \"" + scriptPath + "\" " + text;
			processStartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
			break;
		case BotLanguage.Java:
			processStartInfo.FileName = "java";
			processStartInfo.Arguments = "-jar \"" + scriptPath + "\" " + text;
			break;
		case BotLanguage.JavaScript:
			processStartInfo.FileName = "node";
			processStartInfo.Arguments = "\"" + scriptPath + "\" " + text;
			break;
		case BotLanguage.CSharp:
			if (scriptPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			{
				processStartInfo.FileName = "dotnet";
				processStartInfo.Arguments = "\"" + scriptPath + "\" " + text;
				break;
			}
			if (scriptPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				processStartInfo.FileName = scriptPath;
				processStartInfo.Arguments = text;
				break;
			}
			_logger.LogError("C# bot must be a compiled .dll or .exe file");
			return null;
		case BotLanguage.CPlusPlus:
			if (scriptPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !scriptPath.Contains('.'))
			{
				processStartInfo.FileName = scriptPath;
				processStartInfo.Arguments = text;
				break;
			}
			_logger.LogError("C++ bot must be a compiled executable");
			return null;
		case BotLanguage.Go:
			if (scriptPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !scriptPath.Contains('.'))
			{
				processStartInfo.FileName = scriptPath;
				processStartInfo.Arguments = text;
				break;
			}
			if (scriptPath.EndsWith(".go", StringComparison.OrdinalIgnoreCase))
			{
				processStartInfo.FileName = "go";
				processStartInfo.Arguments = "run \"" + scriptPath + "\" " + text;
				break;
			}
			_logger.LogError("Go bot must be a compiled executable or .go file");
			return null;
		default:
			_logger.LogError($"Unsupported bot language: {language}");
			return null;
		}
		return processStartInfo;
	}

	public async Task<bool> StopBotProcessAsync(int processId)
	{
		try
		{
			if (_activeProcesses.TryGetValue(processId, out Process value))
			{
				if (!value.HasExited)
				{
					value.Kill();
					await Task.Delay(500);
				}
				_activeProcesses.Remove(processId);
				_logger.LogInformation($"Bot process {processId} stopped");
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error stopping bot process {processId}: {ex.Message}");
			return false;
		}
	}

	public bool IsProcessRunning(int processId)
	{
		try
		{
			if (_activeProcesses.TryGetValue(processId, out Process value))
			{
				return !value.HasExited;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public async Task StopAllProcessesAsync()
	{
		_logger.LogInformation($"Stopping all bot processes. Active processes: {_activeProcesses.Count}");
		List<int> list = _activeProcesses.Keys.ToList();
		foreach (int item in list)
		{
			await StopBotProcessAsync(item);
		}
		_logger.LogInformation("All bot processes stopped");
	}
}
