using System;
using Microsoft.Extensions.DependencyInjection;
using Server.Services.IServices;
using Server.Utility;

namespace Server.Services;

public class GameManagerFactory : IGameManagerFactory
{
	private readonly IServiceProvider _serviceProvider;

	public GameManagerFactory(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider;
	}

	public GameManager Create(string gameId, string? player1Name, string? player2Name)
	{
		IAttackService requiredService = _serviceProvider.GetRequiredService<IAttackService>();
		IPlayerServices requiredService2 = _serviceProvider.GetRequiredService<IPlayerServices>();
		IMapServices requiredService3 = _serviceProvider.GetRequiredService<IMapServices>();
		IGameDataService requiredService4 = _serviceProvider.GetRequiredService<IGameDataService>();
		TurnWaitingService requiredService5 = _serviceProvider.GetRequiredService<TurnWaitingService>();
		return new GameManager(gameId, requiredService, requiredService2, requiredService3, requiredService4, requiredService5, player1Name, player2Name);
	}
}
