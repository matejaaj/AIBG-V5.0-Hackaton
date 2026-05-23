using System;
using System.Collections.Generic;
using System.Linq;
using Controllers;
using Newtonsoft.Json;
using SharedLibrary;
using SharedLibrary.DTOs;
using UnityEngine;
using Utility;

namespace HTTP_Client;

public class MessageHandler
{
	private Dictionary<MessageType, Action<string>> _messageHandlers;

	private TurnStateTracker _tracker;

	private PlayerController _playerController;

	public MessageHandler()
	{
		_messageHandlers = new Dictionary<MessageType, Action<string>>
		{
			{
				MessageType.PlayerUpdate,
				UpdatePlayer
			},
			{
				MessageType.PlayerMove,
				UpdateMove
			},
			{
				MessageType.MonsterMove,
				UpdateMonsterMove
			},
			{
				MessageType.MapUpdate,
				UpdateMap
			},
			{
				MessageType.ReloadItems,
				SpawnItems
			},
			{
				MessageType.Attack,
				Attack
			},
			{
				MessageType.ItemPicked,
				PickUpItem
			},
			{
				MessageType.MonsterCardPicked,
				PickUpCard
			},
			{
				MessageType.TurnChanged,
				TurnChanged
			},
			{
				MessageType.TurnCounterUpdate,
				TurnCounterUpdate
			},
			{
				MessageType.SetMaxTurn,
				SetMaxTurn
			},
			{
				MessageType.MonsterSummoned,
				MonsterSummoned
			},
			{
				MessageType.ItemUsed,
				ItemUsed
			},
			{
				MessageType.ItemUsedUp,
				ItemUsedUp
			},
			{
				MessageType.CardUsedUp,
				CardUsedUp
			},
			{
				MessageType.CardUsed,
				CardUsed
			},
			{
				MessageType.Error,
				ServerError
			},
			{
				MessageType.GameOver,
				GameOver
			}
		};
	}

	public void Reset()
	{
		_tracker = null;
		_playerController = null;
	}

	private TurnStateTracker getTurnStateTracker()
	{
		if (_tracker != null)
		{
			return _tracker;
		}
		GameObject gameObject = GameObject.Find("TurnStateTracker");
		if (gameObject != null)
		{
			_tracker = gameObject.GetComponent<TurnStateTracker>();
		}
		if (_tracker == null)
		{
			Debug.LogWarning("TurnStateTracker is not ready yet.");
		}
		return _tracker;
	}

	private PlayerController getPlayerController()
	{
		if (_playerController == null)
		{
			GameObject gameObject = GameObject.FindWithTag("PlayerController");
			if (gameObject != null)
			{
				_playerController = gameObject.GetComponent<PlayerController>();
			}
		}
		if (_playerController == null)
		{
			Debug.LogWarning("PlayerController is not ready yet.");
		}
		return _playerController;
	}

	public void HandleMessage(string message)
	{
		Message message2 = ParseMessage(message);
		if (message2 == null)
		{
			Debug.Log("Invalid message format received.");
			return;
		}
		MessageType type = message2.Type;
		string data = message2.Data;
		if (!_messageHandlers.TryGetValue(type, out var value))
		{
			return;
		}
		try
		{
			value(data);
		}
		catch (Exception ex)
		{
			Debug.LogError($"Message handler failed for {type}: {ex.ToString()}");
		}
	}

	public void TurnCounterUpdate(string data)
	{
		if (!(getTurnStateTracker() == null))
		{
			int turnNumber = JsonConvert.DeserializeObject<int>(data);
			_tracker.UpdateTurn(turnNumber);
		}
	}

	public void SetMaxTurn(string data)
	{
		if (!(getTurnStateTracker() == null))
		{
			int maxTurn = JsonConvert.DeserializeObject<int>(data);
			_tracker.SetMaxTurn(maxTurn);
		}
	}

	public void GameOver(string data)
	{
		Player player = JsonConvert.DeserializeObject<Player>(data);
		GameManager.Instance.GameOver(player);
	}

	private void Attack(string data)
	{
		getPlayerController();
		AttackDTO attackDTO = JsonConvert.DeserializeObject<AttackDTO>(data);
		GameManager.Instance.GetEntityWrapper(attackDTO.attacker.Id).EntityUpdate(attackDTO.attacker);
		foreach (IEntityWrapper attackedWrapper in attackDTO.attacked.Select((Entity entity) => GameManager.Instance.GetEntityWrapper(entity.Id)))
		{
			attackedWrapper.TriggerTakeDamageAnim();
			attackedWrapper.EntityUpdate(attackDTO.attacked.Find((Entity entity) => entity.Id == attackedWrapper.Id));
		}
	}

	private void UpdatePlayer(string data)
	{
		if (getPlayerController() == null)
		{
			return;
		}
		Player player = JsonConvert.DeserializeObject<Player>(data);
		GameManager.Instance.GetEntityWrapper(player.Id);
		if (PlayerController.Instance.GetPlayer1().Id != player.Id)
		{
			PlayerWrapper playerWrapper = PlayerController.Instance.GetPlayerWrapper(player.Id);
			if (playerWrapper.Id != player.Id)
			{
				Debug.LogError("MessageHandler/UpdatePlayer: Player Id is invalid");
			}
			PlayerController.Instance.setPlayer2(player);
			PlayerController.Instance.ReloadStatsPlayer2();
			if (playerWrapper.player.HasStatus(Status.Frozen))
			{
				playerWrapper.ShowIce();
			}
			else
			{
				playerWrapper.HideIce();
			}
			if (playerWrapper.player.HasStatus(Status.Dizzy))
			{
				playerWrapper.ShowConfused();
			}
			else
			{
				playerWrapper.HideConfused();
			}
		}
		else
		{
			PlayerWrapper playerWrapper2 = PlayerController.Instance.GetPlayerWrapper(player.Id);
			PlayerController.Instance.setPlayer1(player);
			PlayerController.Instance.ReloadStatsPlayer1();
			if (playerWrapper2.player.HasStatus(Status.Frozen))
			{
				playerWrapper2.ShowIce();
			}
			else
			{
				playerWrapper2.HideIce();
			}
			if (playerWrapper2.player.HasStatus(Status.Dizzy))
			{
				playerWrapper2.ShowConfused();
			}
			else
			{
				playerWrapper2.HideConfused();
			}
		}
	}

	public void TakeDamageAnimation(PlayerWrapper playerWrapper)
	{
		playerWrapper.TriggerTakeDamageAnim();
	}

	private void ItemUsed(string data)
	{
		if (!(getPlayerController() == null))
		{
			Item item = JsonConvert.DeserializeObject<Item>(data);
			PlayerController.Instance.ReloadItem(item);
		}
	}

	private void ItemUsedUp(string data)
	{
		if (getPlayerController() == null)
		{
			return;
		}
		Player currentPlayer = PlayerController.Instance.GetCurrentPlayer();
		if (currentPlayer == null)
		{
			Debug.LogWarning("ItemUsed: Current player is null.");
			return;
		}
		Item updatedItem = JsonConvert.DeserializeObject<Item>(data);
		if (currentPlayer.Id == PlayerController.Instance.GetPlayer1().Id)
		{
			PlayerController.Instance.GetPlayer1().Inventory.RemoveAll((Item item) => item.Id == updatedItem.Id);
			PlayerController.Instance.ReloadItemsPlayer1();
		}
		else if (currentPlayer.Id == PlayerController.Instance.GetPlayer2().Id)
		{
			PlayerController.Instance.GetPlayer2().Inventory.RemoveAll((Item item) => item.Id == updatedItem.Id);
			PlayerController.Instance.ReloadItemsPlayer2();
		}
	}

	private void UpdateMap(string data)
	{
		if (MapController.Instance == null)
		{
			Debug.LogWarning("Map update skipped because MapController is not ready.");
			return;
		}
		List<FieldInfo> fields = JsonConvert.DeserializeObject<List<FieldInfo>>(data);
		MapController.Instance.UpdateMap(fields);
	}

	private void SpawnItems(string data)
	{
		List<FieldInfo>? list = JsonConvert.DeserializeObject<List<FieldInfo>>(data);
		MapController.Instance.RemoveItemsAndCards();
		foreach (FieldInfo item in list)
		{
			GameObject fieldGameObject = MapController.Instance.GetFieldGameObject(item.Position);
			if (!(fieldGameObject == null))
			{
				MapController.Instance.InstantiateFieldContent(fieldGameObject, item, default(Vector3));
			}
		}
	}

	private void UpdateMove(string data)
	{
		if (PlayerController.Instance == null)
		{
			Debug.LogWarning("MessageHandler/UpdateMove: PlayerController.Instance is null");
			return;
		}
		if (GameManager.Instance == null)
		{
			Debug.LogWarning("MessageHandler/UpdateMove: GameManager.Instance is null");
			return;
		}
		Player player = JsonConvert.DeserializeObject<Player>(data);
		if (player == null)
		{
			Debug.LogError("MessageHandler/UpdateMove: Invalid player payload");
			return;
		}
		IEntityWrapper entityWrapper = GameManager.Instance.GetEntityWrapper(player.Id);
		if (entityWrapper == null)
		{
			Debug.LogError($"MessageHandler/UpdateMove: Player wrapper not found for ID: {player.Id}");
			return;
		}
		entityWrapper.Move(player.Position);
		PlayerController.Instance.HideMovementWarning();
	}

	private void UpdateMonsterMove(string data)
	{
		Entity entity = JsonConvert.DeserializeObject<Entity>(data);
		if (entity == null)
		{
			Debug.LogError("MessageHandler/UpdateMonsterMove: Invalid monster payload");
			return;
		}
		IEntityWrapper entityWrapper = GameManager.Instance.GetEntityWrapper(entity.Id);
		if (entityWrapper == null)
		{
			Debug.LogError("MessageHandler/UpdateMonsterMove: Monster wrapper not found");
			return;
		}
		entityWrapper.Move(entity.Position);
		entityWrapper.EntityUpdate(entity);
	}

	private void TurnChanged(string data)
	{
		if (!(PlayerController.Instance == null) && !(getTurnStateTracker() == null))
		{
			TurnState turnState = JsonConvert.DeserializeObject<TurnState>(data);
			while (!(getTurnStateTracker() != null))
			{
			}
			getTurnStateTracker().ChangeTurn(turnState);
			PlayerController.Instance.LockOtherPlayer();
			PlayerController.Instance.HideMovementWarning();
			if (turnState == TurnState.Player1Turn || turnState == TurnState.Player2Turn)
			{
				PlayerController.Instance.clickable = true;
			}
		}
	}

	private void PickUpItem(string data)
	{
		JsonConvert.DeserializeObject<Player>(data);
		UpdatePlayer(data);
		if (_playerController == null)
		{
			return;
		}
		Player currentPlayer = PlayerController.Instance.GetCurrentPlayer();
		if (currentPlayer == null)
		{
			Debug.LogWarning("PickUpItem: Current player is null.");
			return;
		}
		if (currentPlayer.Id == PlayerController.Instance.GetPlayer1().Id)
		{
			PlayerController.player1Prefab.GetComponent<PlayerWrapper>().TriggerPickUpItemAnim();
			PlayerController.Instance.ReloadItemsPlayer1();
		}
		else if (currentPlayer.Id == PlayerController.Instance.GetPlayer2().Id)
		{
			PlayerController.player2Prefab.GetComponent<PlayerWrapper>().TriggerPickUpItemAnim();
			PlayerController.Instance.ReloadItemsPlayer2();
		}
		PlayerController.fieldOfInterest.GetComponent<FieldInfoWrapper>().fieldInfo.Item = null;
	}

	private void PickUpCard(string data)
	{
		JsonConvert.DeserializeObject<Player>(data);
		UpdatePlayer(data);
		if (!(PlayerController.Instance == null))
		{
			PlayerWrapper currentPlayerWrapper = PlayerController.Instance.GetCurrentPlayerWrapper();
			currentPlayerWrapper.TriggerPickUpCardAnim();
			currentPlayerWrapper.ReloadCards();
			PlayerController.fieldOfInterest.GetComponent<FieldInfoWrapper>().fieldInfo.MonsterCard = null;
			Debug.Log("Card picked up!");
		}
	}

	private void MonsterSummoned(string data)
	{
		Entity entity = JsonConvert.DeserializeObject<Entity>(data);
		GameObject fieldGameObject = MapController.Instance.GetFieldGameObject(entity.Position);
		Debug.Log(fieldGameObject.ToString());
		MonsterSpawner.Instance.InstantiateMonster(entity, fieldGameObject);
		fieldGameObject.GetComponent<FieldInfoWrapper>().fieldInfo.Entity = entity;
	}

	private void CardUsed(string data)
	{
		if (!(getPlayerController() == null))
		{
			Debug.Log("Card Used");
			MonsterCard card = JsonConvert.DeserializeObject<MonsterCard>(data);
			PlayerController.Instance.ReloadCard(card);
		}
	}

	private void CardUsedUp(string data)
	{
		if (getPlayerController() == null)
		{
			return;
		}
		Debug.Log("CardUsedUp");
		MonsterCard usedCard = JsonConvert.DeserializeObject<MonsterCard>(data);
		Player currentPlayer = PlayerController.Instance.GetCurrentPlayer();
		if (currentPlayer == null)
		{
			Debug.LogWarning("CardUsedUp: Current player is null.");
		}
		else if (currentPlayer.Id == PlayerController.Instance.GetPlayer1().Id)
		{
			PlayerController.Instance.GetPlayer1().Cards.RemoveAll((MonsterCard card) => card.Id == usedCard.Id);
			PlayerController.Instance.ReloadCardsPlayer1();
		}
		else
		{
			PlayerController.Instance.GetPlayer2().Cards.RemoveAll((MonsterCard card) => card.Id == usedCard.Id);
			PlayerController.Instance.ReloadCardsPlayer2();
		}
	}

	private void ServerError(string data)
	{
		Debug.LogError("Server error: " + data);
		PlayerController.Instance.LockOtherPlayer();
		PlayerController.Instance.HideMovementWarning();
		PlayerController.Instance.RefreshMoveHighlights();
	}

	private Message ParseMessage(string message)
	{
		try
		{
			return JsonConvert.DeserializeObject<Message>(message);
		}
		catch (Exception ex)
		{
			Debug.Log("Parsing failed: " + ex.Message);
			return null;
		}
	}
}
