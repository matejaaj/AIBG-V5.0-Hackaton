using System.Collections.Generic;
using System.Linq;
using SharedLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Utility;

namespace Controllers;

public class PlayerController : MonoBehaviour
{
	public static GameObject fieldOfInterest;

	[Header("--------Player1--------")]
	[SerializeField]
	private TextMeshProUGUI player1Health;

	[SerializeField]
	private TextMeshProUGUI player1MaxHealth;

	[SerializeField]
	private TextMeshProUGUI player1HealthDown;

	[SerializeField]
	private TextMeshProUGUI player1MaxHealthDown;

	[SerializeField]
	private TextMeshProUGUI player1Monster;

	[SerializeField]
	private TextMeshProUGUI player1Movement;

	[SerializeField]
	private GameObject player1Inventory;

	[SerializeField]
	private GameObject player1Cards;

	[Header("-------Player2---------")]
	[SerializeField]
	private TextMeshProUGUI player2Health;

	[SerializeField]
	private TextMeshProUGUI player2MaxHealth;

	[SerializeField]
	private TextMeshProUGUI player2HealthDown;

	[SerializeField]
	private TextMeshProUGUI player2MaxHealthDown;

	[SerializeField]
	private TextMeshProUGUI player2Monster;

	[SerializeField]
	private TextMeshProUGUI player2Movement;

	[SerializeField]
	private GameObject player2Inventory;

	[SerializeField]
	private GameObject player2Cards;

	[Header("-------OTHER---------")]
	public static GameObject player1Prefab;

	public static GameObject player2Prefab;

	[SerializeField]
	public GameObject MonsterPrefab;

	[SerializeField]
	private ItemSpawner ItemDictionary;

	[SerializeField]
	private CardSpawner CardDictionary;

	[SerializeField]
	private GameObject trackerGameObject;

	private TurnStateTracker tracker;

	[SerializeField]
	private TextMeshProUGUI movementWarning;

	[SerializeField]
	private string movementWarningObjectName = "MovementWarning";

	[SerializeField]
	private GameObject movementWarningObject;

	[SerializeField]
	private float movementWarningDuration = 2f;

	private List<ItemButton> itemButtons1;

	private List<ItemButton> itemButtons2;

	private List<CardButton> cardButtons1;

	private List<CardButton> cardButtons2;

	private float _movementWarningHideAt = -1f;

	public bool clickable = true;

	internal bool summonState;

	internal MonsterCard pendingCard;

	public static PlayerController Instance { get; private set; }

	public void Awake()
	{
		itemButtons1 = player1Inventory.GetComponentsInChildren<ItemButton>().ToList();
		itemButtons2 = player2Inventory.GetComponentsInChildren<ItemButton>().ToList();
		cardButtons1 = player1Cards.GetComponentsInChildren<CardButton>().ToList();
		cardButtons2 = player2Cards.GetComponentsInChildren<CardButton>().ToList();
		tracker = trackerGameObject.GetComponent<TurnStateTracker>();
		if (Instance != null && Instance != this)
		{
			Object.Destroy(base.gameObject);
			return;
		}
		Instance = this;
		ResolveMovementWarningReference();
		HideMovementWarning();
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	private void Update()
	{
		if (movementWarning != null && movementWarning.gameObject.activeSelf && Time.time >= _movementWarningHideAt)
		{
			HideMovementWarning();
		}
		if (!Input.GetMouseButtonDown(0) || !clickable || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) || !Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out var hitInfo))
		{
			return;
		}
		_ = hitInfo.point;
		Debug.Log(hitInfo.collider.tag);
		if (!hitInfo.collider.CompareTag("Field"))
		{
			return;
		}
		fieldOfInterest = hitInfo.collider.gameObject;
		FieldInfo fieldInfo = hitInfo.collider.gameObject.GetComponent<FieldInfoWrapper>().fieldInfo;
		Debug.Log("Clicked on: (" + fieldInfo.Position.X + "," + fieldInfo.Position.Y + ")");
		if (!CanCurrentPlayerMoveTo(fieldInfo))
		{
			ShowMovementWarning("This move was out of bounds");
			return;
		}
		clickable = false;
		HideMovementWarning();
		if (summonState && pendingCard != null)
		{
			GetCurrentPlayerWrapper().SummonMonster(pendingCard, fieldInfo.Position);
			pendingCard = null;
			summonState = false;
		}
		else if (fieldInfo.Item != null)
		{
			Debug.Log(fieldInfo.Position.ToString());
			GetCurrentPlayerWrapper().PickUpItem(fieldInfo);
		}
		else if (fieldInfo.MonsterCard != null)
		{
			Debug.Log(fieldInfo.Position.ToString());
			GetCurrentPlayerWrapper().PickUpCard(fieldInfo);
		}
		else if (fieldInfo.Entity != null)
		{
			Debug.Log(fieldInfo.Entity.Name);
			GetCurrentPlayerWrapper().Attack(fieldInfo.Entity.Id);
		}
		else
		{
			GetCurrentPlayerObject().GetComponent<PlayerWrapper>().MovePlayer(fieldInfo);
		}
	}

	public void RemoveCardsAndItems()
	{
		foreach (ItemButton item in itemButtons1)
		{
			item.TurnOff();
		}
		foreach (ItemButton item2 in itemButtons2)
		{
			item2.TurnOff();
		}
		foreach (CardButton item3 in cardButtons1)
		{
			item3.TurnOff();
		}
		foreach (CardButton item4 in cardButtons2)
		{
			item4.TurnOff();
		}
	}

	public void UpdateText()
	{
		if (player1Prefab == null || player2Prefab == null)
		{
			Debug.LogWarning("UpdateText called but player prefabs are not initialized yet.");
			return;
		}
		Player player = player1Prefab.GetComponent<PlayerWrapper>().player;
		Player player2 = player2Prefab.GetComponent<PlayerWrapper>().player;
		ReloadStatsPlayer1();
		player1Monster.text = player.Cards.Count + "/3";
		player1Movement.text = player.MaxMoveDistance + "/4";
		ReloadItemsPlayer1();
		ReloadItemsPlayer2();
		ReloadCardsPlayer1();
		ReloadCardsPlayer2();
		ReloadStatsPlayer2();
		player2Monster.text = player2.Cards.Count + "/3";
		player2Movement.text = player2.MaxMoveDistance + "/4";
	}

	internal void ReloadCardsPlayer1()
	{
		Player player = player1Prefab.GetComponent<PlayerWrapper>().player;
		List<CardButton> list = cardButtons1.FindAll((CardButton button) => button.button.enabled);
		foreach (MonsterCard card in player.Cards)
		{
			CardWrapper card2 = CardDictionary.GetCard(card.Name);
			if (card2 == null)
			{
				Debug.LogError("No sprite found for card '" + card.Name + "'");
				continue;
			}
			CardButton cardButton = list.FirstOrDefault((CardButton gear) => gear.card.Id == card.Id);
			if (cardButton != null)
			{
				cardButton.ReloadText(card);
				list.Remove(cardButton);
				continue;
			}
			CardButton cardButton2 = cardButtons1.FirstOrDefault((CardButton button) => !button.button.enabled);
			if (cardButton2 != null)
			{
				cardButton2.Load(player.Id, card, card2.sprite);
			}
		}
		foreach (CardButton item in list.FindAll((CardButton button) => button.button.enabled))
		{
			item.TurnOff();
		}
	}

	internal void ReloadCardsPlayer2()
	{
		Player player = player2Prefab.GetComponent<PlayerWrapper>().player;
		List<CardButton> list = cardButtons2.FindAll((CardButton button) => button.button.enabled);
		foreach (MonsterCard card in player.Cards)
		{
			CardWrapper card2 = CardDictionary.GetCard(card.Name);
			if (card2 == null)
			{
				Debug.LogError("No sprite found for card '" + card.Name + "'");
				continue;
			}
			CardButton cardButton = list.FirstOrDefault((CardButton gear) => gear.card.Id == card.Id);
			if (cardButton != null)
			{
				cardButton.ReloadText(card);
				list.Remove(cardButton);
				continue;
			}
			CardButton cardButton2 = cardButtons2.FirstOrDefault((CardButton button) => !button.button.enabled);
			if (cardButton2 != null)
			{
				cardButton2.Load(player.Id, card, card2.sprite);
			}
		}
		foreach (CardButton item in list.FindAll((CardButton button) => button.button.enabled))
		{
			item.TurnOff();
		}
	}

	internal void ReloadCard(MonsterCard card)
	{
		if (GetCurrentPlayer().First)
		{
			CardButton cardButton = cardButtons1.FirstOrDefault((CardButton gear) => gear.card.Id == card.Id);
			if (cardButton != null)
			{
				cardButton.ReloadText(card);
			}
		}
		else
		{
			CardButton cardButton2 = cardButtons2.FirstOrDefault((CardButton gear) => gear.card.Id == card.Id);
			if (cardButton2 != null)
			{
				cardButton2.ReloadText(card);
			}
		}
	}

	internal void ReloadStatsPlayer2()
	{
		Player player = player2Prefab.GetComponent<PlayerWrapper>().player;
		player2Health.text = player.Health.ToString();
		player2MaxHealth.text = player.MaxHealth.ToString();
		player2HealthDown.text = player.Health.ToString();
		player2MaxHealthDown.text = player.MaxHealth.ToString();
	}

	internal void ReloadStatsPlayer1()
	{
		Player player = player1Prefab.GetComponent<PlayerWrapper>().player;
		player1Health.text = player.Health.ToString();
		player1MaxHealth.text = player.MaxHealth.ToString();
		player1HealthDown.text = player.Health.ToString();
		player1MaxHealthDown.text = player.MaxHealth.ToString();
	}

	internal void ReloadItem(Item item)
	{
		ItemButton itemButton = itemButtons1.FirstOrDefault((ItemButton gear) => gear.item.Id == item.Id);
		if (itemButton != null)
		{
			itemButton.ReloadText(item);
			return;
		}
		ItemButton itemButton2 = itemButtons2.FirstOrDefault((ItemButton gear) => gear.item.Id == item.Id);
		if (itemButton2 != null)
		{
			itemButton2.ReloadText(item);
		}
		else
		{
			Debug.LogError("Item you are trying to update doesn't exist");
		}
	}

	internal void ReloadItemsPlayer1()
	{
		Player player = player1Prefab.GetComponent<PlayerWrapper>().player;
		List<ItemButton> list = itemButtons1.FindAll((ItemButton button) => button.button.enabled);
		foreach (Item item in player.Inventory)
		{
			ItemWrapper item2 = ItemDictionary.GetItem(item.Name);
			if (item2 == null)
			{
				Debug.LogError("No sprite found for item '" + item.Name + "'");
				continue;
			}
			ItemButton itemButton = list.FirstOrDefault((ItemButton gear) => gear.item.Id == item.Id);
			if (itemButton != null)
			{
				itemButton.ReloadText(item);
				list.Remove(itemButton);
				continue;
			}
			ItemButton itemButton2 = itemButtons1.FirstOrDefault((ItemButton button) => !button.button.enabled);
			if (itemButton2 != null)
			{
				itemButton2.Load(player.Id, item, item2.sprite);
			}
		}
		foreach (ItemButton item3 in list.FindAll((ItemButton button) => button.button.enabled))
		{
			item3.TurnOff();
		}
	}

	internal void ReloadItemsPlayer2()
	{
		Player player = player2Prefab.GetComponent<PlayerWrapper>().player;
		List<ItemButton> list = itemButtons2.FindAll((ItemButton button) => button.button.enabled);
		foreach (Item item in player.Inventory)
		{
			ItemWrapper item2 = ItemDictionary.GetItem(item.Name);
			if (item2 == null)
			{
				Debug.LogError("No sprite found for item '" + item.Name + "'");
				continue;
			}
			ItemButton itemButton = list.FirstOrDefault((ItemButton gear) => gear.item.Id == item.Id);
			if (itemButton != null)
			{
				itemButton.ReloadText(item);
				list.Remove(itemButton);
				continue;
			}
			ItemButton itemButton2 = itemButtons2.FirstOrDefault((ItemButton button) => !button.button.enabled);
			if (itemButton2 != null)
			{
				itemButton2.Load(player.Id, item, item2.sprite);
			}
		}
		foreach (ItemButton item3 in list.FindAll((ItemButton button) => button.button.enabled))
		{
			item3.TurnOff();
		}
	}

	public Player GetCurrentPlayer()
	{
		if (player1Prefab == null || player2Prefab == null)
		{
			Debug.LogWarning("GetCurrentPlayer called but player prefabs are not initialized yet.");
			return null;
		}
		return tracker.turnState switch
		{
			TurnState.Player1Turn => player1Prefab.GetComponent<PlayerWrapper>().player, 
			TurnState.Player2Turn => player2Prefab.GetComponent<PlayerWrapper>().player, 
			_ => null, 
		};
	}

	public GameObject GetCurrentPlayerObject()
	{
		return tracker.turnState switch
		{
			TurnState.Player1Turn => player1Prefab, 
			TurnState.Player2Turn => player2Prefab, 
			_ => null, 
		};
	}

	public PlayerWrapper GetCurrentPlayerWrapper()
	{
		return tracker.turnState switch
		{
			TurnState.Player1Turn => player1Prefab.GetComponent<PlayerWrapper>(), 
			TurnState.Player2Turn => player2Prefab.GetComponent<PlayerWrapper>(), 
			_ => null, 
		};
	}

	public void LockOtherPlayer()
	{
		switch (tracker.turnState)
		{
		case TurnState.Player1Turn:
			UnLockPlayer1();
			LockPlayer2();
			break;
		case TurnState.Player2Turn:
			LockPlayer1();
			UnLockPlayer2();
			break;
		}
	}

	public void LockPlayer1()
	{
		foreach (CardButton item in cardButtons1)
		{
			item.Lock();
		}
		foreach (ItemButton item2 in itemButtons1)
		{
			item2.Lock();
		}
	}

	public void UnLockPlayer1()
	{
		foreach (CardButton item in cardButtons1)
		{
			item.Unlock();
		}
		foreach (ItemButton item2 in itemButtons1)
		{
			item2.Unlock();
		}
	}

	public void LockPlayer2()
	{
		foreach (CardButton item in cardButtons2)
		{
			item.Lock();
		}
		foreach (ItemButton item2 in itemButtons2)
		{
			item2.Lock();
		}
	}

	public void UnLockPlayer2()
	{
		foreach (CardButton item in cardButtons2)
		{
			item.Unlock();
		}
		foreach (ItemButton item2 in itemButtons2)
		{
			item2.Unlock();
		}
	}

	public PlayerWrapper GetPlayerWrapper(int id)
	{
		if (GetPlayer1().Id == id)
		{
			return player1Prefab.GetComponent<PlayerWrapper>();
		}
		if (GetPlayer2().Id == id)
		{
			return player2Prefab.GetComponent<PlayerWrapper>();
		}
		return null;
	}

	public Player GetPlayer1()
	{
		return player1Prefab.GetComponent<PlayerWrapper>().player;
	}

	public Player GetPlayer2()
	{
		return player2Prefab.GetComponent<PlayerWrapper>().player;
	}

	public void setPlayer1(Player player)
	{
		player1Prefab.GetComponent<PlayerWrapper>().player = player;
	}

	public void setPlayer2(Player player)
	{
		player2Prefab.GetComponent<PlayerWrapper>().player = player;
	}

	public void RefreshMoveHighlights()
	{
		if (!(MapController.Instance == null))
		{
			Player currentPlayer = GetCurrentPlayer();
			if (currentPlayer != null)
			{
				MapController.Instance.RefreshMoveHighlights(currentPlayer);
			}
		}
	}

	private bool CanCurrentPlayerMoveTo(FieldInfo fieldInfo)
	{
		if (MapController.Instance != null && fieldInfo != null)
		{
			return MapController.Instance.IsMoveAllowed(GetCurrentPlayer(), fieldInfo.Position);
		}
		return false;
	}

	public void ShowMovementWarning(string warningText)
	{
		ResolveMovementWarningReference();
		if (!(movementWarning == null))
		{
			movementWarningObject.gameObject.SetActive(value: true);
			movementWarning.text = warningText;
			movementWarning.gameObject.SetActive(value: true);
			_movementWarningHideAt = Time.time + movementWarningDuration;
		}
	}

	public void HideMovementWarning()
	{
		ResolveMovementWarningReference();
		if (!(movementWarning == null))
		{
			movementWarningObject.gameObject.SetActive(value: false);
			movementWarning.gameObject.SetActive(value: false);
			_movementWarningHideAt = -1f;
		}
	}

	private void ResolveMovementWarningReference()
	{
		if (!(movementWarning != null))
		{
			movementWarning = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>().FirstOrDefault((TextMeshProUGUI text) => text != null && text.gameObject.scene.IsValid() && text.gameObject.scene == base.gameObject.scene && text.name == movementWarningObjectName);
		}
	}
}
