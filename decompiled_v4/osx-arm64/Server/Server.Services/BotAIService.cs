using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Services.IServices;
using Server.Utility;
using SharedLibrary;

namespace Server.Services;

public class BotAIService : BackgroundService
{
	private readonly IServiceProvider _serviceProvider;

	private readonly ILogger<BotAIService> _logger;

	private readonly ConcurrentDictionary<string, string> _botGames = new ConcurrentDictionary<string, string>();

	public BotAIService(IServiceProvider serviceProvider, ILogger<BotAIService> logger)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
	}

	public void RegisterBotGame(string gameId, string botPlayerName)
	{
		_botGames.TryAdd(gameId, botPlayerName);
		_logger.LogInformation("Registered bot game: {GameId} with bot player {BotName}", gameId, botPlayerName);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("BotAIService started");
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(100, stoppingToken);
				foreach (KeyValuePair<string, string> botGame in _botGames)
				{
					string gameId = botGame.Key;
					string value = botGame.Value;
					try
					{
						await ProcessBotTurn(gameId, value, stoppingToken);
					}
					catch (Exception exception)
					{
						_logger.LogError(exception, "Error processing bot turn for game {GameId}", gameId);
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception2)
			{
				_logger.LogError(exception2, "Error in BotAIService main loop");
			}
		}
		_logger.LogInformation("BotAIService stopped");
	}

	private async Task ProcessBotTurn(string gameId, string botPlayerName, CancellationToken cancellationToken)
	{
		using IServiceScope scope = _serviceProvider.CreateScope();
		IGameSessionManager requiredService = scope.ServiceProvider.GetRequiredService<IGameSessionManager>();
		IPlayerServices requiredService2 = scope.ServiceProvider.GetRequiredService<IPlayerServices>();
		IMapServices requiredService3 = scope.ServiceProvider.GetRequiredService<IMapServices>();
		TurnWaitingService requiredService4 = scope.ServiceProvider.GetRequiredService<TurnWaitingService>();
		GameManager gameById = requiredService.GetGameById(gameId);
		if (gameById == null)
		{
			_botGames.TryRemove(gameId, out string _);
			return;
		}
		Player player = gameById.Players.Values.FirstOrDefault((Player p) => p.Name == botPlayerName);
		if (player != null && gameById.TryClaimTurn(player))
		{
			_logger.LogInformation("Bot's turn detected for game {GameId}, player {BotName} (ID: {PlayerId})", gameId, botPlayerName, player.Id);
			await MakeBotMove(gameById, player, requiredService2, requiredService3, requiredService4, gameId);
		}
	}

	private async Task MakeBotMove(GameManager game, Player botPlayer, IPlayerServices playerServices, IMapServices mapServices, TurnWaitingService turnWaitingService, string gameId)
	{
		bool shouldEndTurn = true;
		try
		{
			Player opponent = game.Players.Values.FirstOrDefault((Player p) => p.Id != botPlayer.Id);
			if (opponent == null)
			{
				_logger.LogWarning("No opponent found for bot player {PlayerId}", botPlayer.Id);
				return;
			}
			await Task.Delay(100);
			bool flag = false;
			if (!flag && CanAttack(botPlayer, opponent))
			{
				try
				{
					IAttackService service = GetService<IAttackService>(gameId);
					_logger.LogInformation("Bot attacking opponent {OpponentId}", opponent.Id);
					service.Attack(gameId, botPlayer.Id, opponent.Id);
					flag = true;
				}
				catch (Exception exception)
				{
					_logger.LogError(exception, "Bot failed to attack opponent");
				}
			}
			if (!flag && TryPickupNearbyItems(game, botPlayer, playerServices, gameId))
			{
				flag = true;
			}
			if (!flag && TryUseOffensiveItem(botPlayer, playerServices, gameId))
			{
				flag = true;
			}
			if (!flag && TrySummonMonster(game, botPlayer, gameId))
			{
				flag = true;
			}
			if (!flag)
			{
				List<Position> allValidMoves = playerServices.GetAllValidMoves(gameId, botPlayer.Id);
				if (allValidMoves != null && allValidMoves.Any())
				{
					try
					{
						Position bestMoveTowardsTarget = GetBestMoveTowardsTarget(allValidMoves, botPlayer.Position, opponent.Position);
						MoveRequest moveRequest = new MoveRequest
						{
							playerId = botPlayer.Id,
							newPosition = bestMoveTowardsTarget
						};
						_logger.LogInformation("Bot moving player {PlayerId} to ({X}, {Y}) towards opponent", botPlayer.Id, bestMoveTowardsTarget.X, bestMoveTowardsTarget.Y);
						playerServices.Move(gameId, botPlayer.Id, moveRequest);
						flag = true;
					}
					catch (Exception exception2)
					{
						_logger.LogError(exception2, "Bot failed to move player {PlayerId}", botPlayer.Id);
					}
				}
				else
				{
					_logger.LogWarning("Bot player {PlayerId} has no valid moves", botPlayer.Id);
				}
			}
			_logger.LogInformation("Bot completed actions for player {PlayerId}, action taken: {ActionTaken}", botPlayer.Id, flag);
		}
		catch (Exception exception3)
		{
			_logger.LogError(exception3, "Error making bot move for player {PlayerId}", botPlayer.Id);
		}
		finally
		{
			if (shouldEndTurn)
			{
				_logger.LogInformation("Bot ending turn for player {PlayerId} in game {GameId}", botPlayer.Id, gameId);
				try
				{
					turnWaitingService.EndTurnProgrammatically(gameId);
				}
				catch (Exception exception4)
				{
					_logger.LogError(exception4, "Failed to end turn for bot player {PlayerId}", botPlayer.Id);
				}
			}
		}
	}

	private bool CanAttack(Player botPlayer, Player opponent)
	{
		if (botPlayer.HasStatus(Status.Fatigue))
		{
			return false;
		}
		int num = Math.Abs(botPlayer.Position.X - opponent.Position.X) + Math.Abs(botPlayer.Position.Y - opponent.Position.Y);
		return num <= botPlayer.AttackRange;
	}

	private Position GetBestMoveTowardsTarget(List<Position> validMoves, Position currentPos, Position targetPos)
	{
		Position result = validMoves[0];
		int num = GetManhattanDistance(validMoves[0], targetPos);
		foreach (Position validMove in validMoves)
		{
			int manhattanDistance = GetManhattanDistance(validMove, targetPos);
			if (manhattanDistance < num)
			{
				num = manhattanDistance;
				result = validMove;
			}
		}
		return result;
	}

	private int GetManhattanDistance(Position from, Position to)
	{
		return Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y);
	}

	private bool TryPickupNearbyItems(GameManager game, Player botPlayer, IPlayerServices playerServices, string gameId)
	{
		int[] array = new int[4] { 1, -1, 0, 0 };
		int[] array2 = new int[4] { 0, 0, 1, -1 };
		for (int i = 0; i < array.Length; i++)
		{
			Position checkPos = new Position(botPlayer.Position.X + array[i], botPlayer.Position.Y + array2[i]);
			if (checkPos.X < 0 || checkPos.X >= game.map.X || checkPos.Y < 0 || checkPos.Y >= game.map.Y)
			{
				continue;
			}
			FieldInfo fieldInfo = game.map.Grid.Find((FieldInfo f) => f.Position != null && f.Position.Equals(checkPos));
			if (fieldInfo == null || fieldInfo.FieldType == Field.OBSTACLE || fieldInfo.FieldType == Field.OBSTACLE_SLOW)
			{
				continue;
			}
			if (fieldInfo.Item != null)
			{
				try
				{
					_logger.LogInformation("Bot {PlayerName} (ID: {PlayerId}) picking up item {ItemName} at ({X}, {Y})", botPlayer.Name, botPlayer.Id, fieldInfo.Item.Name, checkPos.X, checkPos.Y);
					playerServices.PickupItem(gameId, botPlayer.Id, fieldInfo);
					return true;
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "Bot failed to pick up item at ({X}, {Y})", checkPos.X, checkPos.Y);
				}
			}
			if (fieldInfo.MonsterCard != null)
			{
				try
				{
					_logger.LogInformation("Bot {PlayerName} (ID: {PlayerId}) picking up monster card {CardName} at ({X}, {Y})", botPlayer.Name, botPlayer.Id, fieldInfo.MonsterCard.Name, checkPos.X, checkPos.Y);
					playerServices.PickupCard(gameId, botPlayer.Id, fieldInfo);
					_logger.LogInformation("Bot successfully picked up card. Cards count: {CardCount}", botPlayer.Cards?.Count ?? 0);
					return true;
				}
				catch (Exception exception2)
				{
					_logger.LogWarning(exception2, "Bot failed to pick up monster card at ({X}, {Y})", checkPos.X, checkPos.Y);
				}
			}
		}
		return false;
	}

	private bool TryUseOffensiveItem(Player botPlayer, IPlayerServices playerServices, string gameId)
	{
		Item item2 = botPlayer.Inventory.FirstOrDefault((Item item) => item.Uses.HasValue && item.Uses > 0 && (item.ItemType == ItemType.Attack || item.ItemType == ItemType.CrowdControl));
		if (item2 != null)
		{
			try
			{
				_logger.LogInformation("Bot using item {ItemName} (ID: {ItemId})", item2.Name, item2.Id);
				playerServices.UseItem(gameId, botPlayer.Id, item2.Id);
				return true;
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Error using offensive item");
			}
		}
		return false;
	}

	private bool TrySummonMonster(GameManager game, Player botPlayer, string gameId)
	{
		if (botPlayer.Cards == null || !botPlayer.Cards.Any())
		{
			return false;
		}
		MonsterCard monsterCard = botPlayer.Cards.FirstOrDefault((MonsterCard c) => !c.OnCooldown);
		if (monsterCard == null)
		{
			return false;
		}
		int[] array = new int[4] { 1, -1, 0, 0 };
		int[] array2 = new int[4] { 0, 0, 1, -1 };
		for (int i = 0; i < array.Length; i++)
		{
			Position summonPos = new Position(botPlayer.Position.X + array[i], botPlayer.Position.Y + array2[i]);
			if (summonPos.X < 0 || summonPos.X >= game.map.X || summonPos.Y < 0 || summonPos.Y >= game.map.Y)
			{
				continue;
			}
			FieldInfo fieldInfo = game.map.Grid.Find((FieldInfo f) => f.Position.Equals(summonPos));
			if (fieldInfo != null && fieldInfo.Entity == null)
			{
				try
				{
					IMonsterService service = GetService<IMonsterService>(gameId);
					_logger.LogInformation("Bot summoning {MonsterName} at ({X}, {Y})", monsterCard.Name, summonPos.X, summonPos.Y);
					service.Summon(gameId, botPlayer.Id, monsterCard.Id, summonPos);
					return true;
				}
				catch (Exception exception)
				{
					_logger.LogError(exception, "Error summoning monster");
				}
			}
		}
		return false;
	}

	private T GetService<T>(string gameId) where T : notnull
	{
		using IServiceScope serviceScope = _serviceProvider.CreateScope();
		return serviceScope.ServiceProvider.GetRequiredService<T>();
	}
}
