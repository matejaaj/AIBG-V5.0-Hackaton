using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using Server;
using Server.Services;
using Server.Services.IServices;
using Server.Utility;

[CompilerGenerated]
internal class Program
{
	private static void _003CMain_003E_0024(string[] args)
	{
		WebApplicationBuilder webApplicationBuilder = WebApplication.CreateBuilder(args);
		webApplicationBuilder.Services.AddControllers().AddNewtonsoftJson(delegate(MvcNewtonsoftJsonOptions o)
		{
			o.SerializerSettings.ContractResolver = new DefaultContractResolver();
		});
		webApplicationBuilder.Services.AddSingleton<IGameSessionManager, GameSessionManager>();
		webApplicationBuilder.Services.AddSingleton<IGameDataService, GameDataService>();
		webApplicationBuilder.Services.AddTransient<IMonsterService, MonsterService>();
		webApplicationBuilder.Services.AddTransient<IMonsterAIStrategy, MinimaxMonsterAIStrategy>();
		webApplicationBuilder.Services.AddTransient<IAttackService, AttackService>();
		webApplicationBuilder.Services.AddTransient<IMonsterService, MonsterService>();
		webApplicationBuilder.Services.AddTransient<IPlayerServices, PlayerServices>();
		webApplicationBuilder.Services.AddTransient<IMapServices, MapServices>();
		webApplicationBuilder.Services.AddSingleton<TurnWaitingService>();
		webApplicationBuilder.Services.AddTransient<IGameManagerFactory, GameManagerFactory>();
		webApplicationBuilder.Services.AddTransient<IBotProcessService, BotProcessService>();
		webApplicationBuilder.Services.AddSingleton<BotAIService>();
		webApplicationBuilder.Services.AddHostedService((IServiceProvider sp) => sp.GetRequiredService<BotAIService>());
		webApplicationBuilder.Services.AddSingleton(new BotSettings
		{
			PrebuiltBotExecutablePath = (webApplicationBuilder.Configuration["BotSettings:PrebuiltBotExecutablePath"] ?? "Bots/PrebuiltBot.ps1"),
			ServerBaseUrl = (webApplicationBuilder.Configuration["BotSettings:ServerBaseUrl"] ?? "http://localhost:8080")
		});
		webApplicationBuilder.Services.AddSingleton<WebSocketMangaer>();
		List<WebSocket> list = new List<WebSocket>();
		webApplicationBuilder.Services.AddEndpointsApiExplorer();
		webApplicationBuilder.Services.AddSwaggerGen();
		WebApplication app = webApplicationBuilder.Build();
		IHostApplicationLifetime requiredService = app.Services.GetRequiredService<IHostApplicationLifetime>();
		requiredService.ApplicationStopping.Register(delegate
		{
			ILogger<Program> requiredService2 = app.Services.GetRequiredService<ILogger<Program>>();
			requiredService2.LogInformation("Server is shutting down, cleaning up bot processes...");
			IBotProcessService requiredService3 = app.Services.GetRequiredService<IBotProcessService>();
			requiredService3.StopAllProcessesAsync().GetAwaiter().GetResult();
			requiredService2.LogInformation("Bot process cleanup completed");
		});
		if (app.Environment.IsDevelopment())
		{
			app.UseSwagger();
			app.UseSwaggerUI();
		}
		app.UseWebSockets();
		app.UseHttpsRedirection();
		app.Use(async delegate(HttpContext context, Func<Task> next)
		{
			ILogger logger2 = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HttpRequest");
			DateTimeOffset started = DateTimeOffset.UtcNow;
			logger2.LogInformation("HTTP {Method} {Path}{QueryString} from {RemoteIp}", context.Request.Method, context.Request.Path.Value, context.Request.QueryString.Value, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
			try
			{
				await next();
			}
			finally
			{
				logger2.LogInformation("HTTP {Method} {Path}{QueryString} completed with {StatusCode} in {ElapsedMs} ms", context.Request.Method, context.Request.Path.Value, context.Request.QueryString.Value, context.Response.StatusCode, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
			}
		});
		app.UseAuthorization();
		app.MapControllers();
		app.Map("/ws/game", (Func<HttpContext, WebSocketMangaer, IGameSessionManager, Task>)async delegate(HttpContext context, WebSocketMangaer something, IGameSessionManager gameSessionManager)
		{
			ILogger logger3 = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GameWebSocket");
			string gameId2 = context.Request.Query["gameId"].ToString();
			logger3.LogInformation("WebSocket connect attempt to {Path} with gameId={GameId} from {RemoteIp}", context.Request.Path.Value, gameId2, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
			if (context.WebSockets.IsWebSocketRequest)
			{
				if (string.IsNullOrEmpty(gameId2))
				{
					logger3.LogWarning("WebSocket rejected because gameId was missing");
					context.Response.StatusCode = 400;
				}
				else
				{
					GameManager game = gameSessionManager.GetGameById(gameId2);
					if (game == null)
					{
						logger3.LogWarning("WebSocket rejected because gameId {GameId} was not found", gameId2);
						context.Response.StatusCode = 404;
					}
					else
					{
						using WebSocket webSocket2 = await context.WebSockets.AcceptWebSocketAsync();
						logger3.LogInformation("WebSocket accepted for gameId {GameId}", gameId2);
						something.AddSocket(gameId2, webSocket2);
						game.StartGame();
						await HandleWebSocketConnection(webSocket2, logger3, gameId2);
						WebSocketMangaer.RemoveSocket(gameId2);
						logger3.LogInformation("WebSocket cleanup finished for gameId {GameId}", gameId2);
					}
				}
			}
			else
			{
				logger3.LogWarning("Non-WebSocket request received at /ws/game");
				context.Response.StatusCode = 400;
			}
		});
		app.Run();
		static async Task HandleWebSocketConnection(WebSocket webSocket, ILogger logger, string gameId)
		{
			byte[] buffer = new byte[4096];
			while (webSocket.State == WebSocketState.Open)
			{
				try
				{
					WebSocketReceiveResult webSocketReceiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
					if (webSocketReceiveResult.MessageType == WebSocketMessageType.Text)
					{
						string @string = Encoding.UTF8.GetString(buffer, 0, webSocketReceiveResult.Count);
						logger.LogInformation("WebSocket message received for gameId {GameId}: {Message}", gameId, @string);
						string s = "Server received: " + @string;
						byte[] bytes = Encoding.UTF8.GetBytes(s);
						await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
					}
					else if (webSocketReceiveResult.MessageType == WebSocketMessageType.Close)
					{
						logger.LogInformation("WebSocket close requested for gameId {GameId}", gameId);
						break;
					}
				}
				catch (WebSocketException exception)
				{
					logger.LogInformation(exception, "WebSocket disconnected unexpectedly for gameId {GameId}", gameId);
					break;
				}
				catch (OperationCanceledException exception2)
				{
					logger.LogInformation(exception2, "WebSocket receive cancelled for gameId {GameId}", gameId);
					break;
				}
			}
		}
	}
}
