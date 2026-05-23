using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Main_Menu_Scripts;
using Newtonsoft.Json;
using SharedLibrary;
using SharedLibrary.DTOs;
using UnityEngine;
using UnityEngine.UI;
using Utility;

namespace Controllers;

public class MapController : MonoBehaviour
{
	public struct HighlightedInteraction
	{
		public Position Position { get; }

		public Color HighlightColor { get; }

		public HighlightedInteraction(Position position, Color highlightColor)
		{
			Position = position;
			HighlightColor = highlightColor;
		}
	}

	[Serializable]
	private class BotVsBotStartRequest
	{
		public string Bot1Name;

		public string Bot2Name;

		public string Bot1ScriptPath;

		public string Bot2ScriptPath;

		public string Bot1Language;

		public string Bot2Language;
	}

	[Serializable]
	private class PlayerVsBotStartRequest
	{
		public string PlayerName;

		public string BotName;

		public string BotScriptPath;

		public string BotLanguage;
	}

	[Header("Prefabs")]
	[SerializeField]
	private GameObject originalTile;

	[SerializeField]
	private GameObject playerPrefab;

	[SerializeField]
	private GameObject playerMalePrefab;

	[SerializeField]
	private GameObject player2Prefab;

	[SerializeField]
	private GameObject player2MalePrefab;

	[SerializeField]
	private GameObject monsterPrefab;

	[SerializeField]
	private ItemSpawner itemSpawner;

	[SerializeField]
	private CardSpawner cardSpawner;

	[Header("Images")]
	[SerializeField]
	private GameObject portraitPlayer1;

	[SerializeField]
	private Sprite player1Female;

	[SerializeField]
	private Sprite player1Male;

	[SerializeField]
	private GameObject portraitPlayer2;

	[SerializeField]
	private Sprite player2Female;

	[SerializeField]
	private Sprite player2Male;

	private readonly Dictionary<Position, GameObject> _fieldObjectMap = new Dictionary<Position, GameObject>();

	private float offsetX = 0.65f;

	private float offsetY = 0.616f;

	private string URL3 = "http://localhost:8080/game/start";

	[Header("Loading")]
	[SerializeField]
	[Min(1f)]
	private int mapBuildBatchSize = 40;

	[Header("Rendering")]
	public Material stronghold;

	public Material normal;

	public Material obstacleSlow;

	public Material trap;

	public Material powerup;

	public Material wall;

	public GameObject SlowObstacle;

	public GameObject Obstacle;

	[Header("Highlighting")]
	[SerializeField]
	private Color movableFieldColor = new Color(0.25f, 0.45f, 0.95f, 1f);

	[SerializeField]
	private Color itemInteractFieldColor = new Color(0.95f, 0.85f, 0.2f, 1f);

	[SerializeField]
	private Color cardInteractFieldColor = new Color(0.95f, 0.85f, 0.2f, 1f);

	[SerializeField]
	private Color attackFieldColor = new Color(0.65f, 0.35f, 0.9f, 1f);

	[SerializeField]
	private Color ObstacleFieldColor = new Color(0f, 0.1f, 0.9f, 1f);

	[SerializeField]
	private Color TrapFieldColor = new Color(1f, 0f, 0.3f, 1f);

	[SerializeField]
	private Color SummonFieldColor = new Color(0.7f, 0.5f, 0f, 1f);

	[SerializeField]
	private Color nonMovableFieldColor = new Color(0.52f, 0.52f, 0.52f, 1f);

	[SerializeField]
	[Range(0f, 1f)]
	private float highlightBlend = 0.45f;

	private readonly Dictionary<GameObject, Color> _originalTileColors = new Dictionary<GameObject, Color>();

	private readonly List<GameObject> _highlightedTiles = new List<GameObject>();

	public static MapController Instance { get; private set; }

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}
		else
		{
			Instance = this;
		}
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	private async void Start()
	{
		await LoadMap();
	}

	private async Task LoadMap()
	{
		Map map;
		try
		{
			GameEssentialsDTO gameEssentialsDTO = await NewGame();
			if (gameEssentialsDTO == null)
			{
				Debug.LogError("Game start request returned null response.");
				return;
			}
			map = gameEssentialsDTO.map;
			if (GameManager.Instance != null)
			{
				GameManager.Instance._gameId = gameEssentialsDTO.gameId;
				GameManager.Instance._webSocketClient.ConnectToServer(gameEssentialsDTO.gameId);
			}
			else
			{
				Debug.LogWarning("GameManager instance is missing in scene. Map will load, but game actions requiring gameId may fail.");
			}
		}
		catch (Exception ex)
		{
			Debug.LogError("Error during game start: " + ex.Message);
			return;
		}
		if (map == null)
		{
			Debug.Log("Failed to load map");
			return;
		}
		Debug.Log("Map Loaded: " + map.Name);
		try
		{
			await InstantiateMapTilesAsync(map);
		}
		catch (Exception ex2)
		{
			Debug.LogError("Error during map instantiation: " + ex2.Message);
			return;
		}
		PlayerController.Instance.UpdateText();
		PlayerController.Instance.RefreshMoveHighlights();
	}

	public async Task ResetAndReload()
	{
		ClearMoveHighlights();
		foreach (GameObject value in _fieldObjectMap.Values)
		{
			if (value != null)
			{
				UnityEngine.Object.Destroy(value);
			}
		}
		_fieldObjectMap.Clear();
		_originalTileColors.Clear();
		_highlightedTiles.Clear();
		foreach (KeyValuePair<int, MonsterWrapper> spawnedMonster in MonsterSpawner.Instance.spawnedMonsters)
		{
			if (spawnedMonster.Value.gameObject != null)
			{
				UnityEngine.Object.Destroy(spawnedMonster.Value.gameObject);
			}
		}
		MonsterSpawner.Instance.spawnedMonsters.Clear();
		if (PlayerController.player1Prefab != null)
		{
			UnityEngine.Object.Destroy(PlayerController.player1Prefab);
		}
		if (PlayerController.player2Prefab != null)
		{
			UnityEngine.Object.Destroy(PlayerController.player2Prefab);
		}
		PlayerController.player1Prefab = null;
		PlayerController.player2Prefab = null;
		PlayerController.Instance.RemoveCardsAndItems();
		await LoadMap();
		if (PlayerController.player1Prefab != null || PlayerController.player2Prefab != null)
		{
			PlayerController.Instance.RefreshMoveHighlights();
		}
		else
		{
			Debug.LogWarning("Players not instantiated after LoadMap! Highlights skipped.");
		}
	}

	public GameObject GetFieldGameObject(Position position)
	{
		_fieldObjectMap.TryGetValue(position, out var value);
		return value;
	}

	private async Task InstantiateMapTilesAsync(Map map)
	{
		Vector3 mapStartPosition = new Vector3(-7.3f, 0.23f, -4.9f);
		int spawnedTiles = 0;
		foreach (FieldInfo item in map.Grid)
		{
			GameObject gameObject = UnityEngine.Object.Instantiate(originalTile);
			float x = mapStartPosition.x + (float)item.Position.X * offsetX;
			float z = mapStartPosition.z + (float)item.Position.Y * offsetY;
			gameObject.transform.position = new Vector3(x, mapStartPosition.y, z);
			_fieldObjectMap.Add(item.Position, gameObject);
			gameObject.AddComponent<FieldInfoWrapper>();
			gameObject.GetComponent<FieldInfoWrapper>().fieldInfo = item;
			InstantiateFieldContent(gameObject, item, mapStartPosition);
			SetTileMaterial(gameObject, item.FieldType);
			CacheTileColor(gameObject);
			if (item.Position.Equals(new Position(0, 0)))
			{
				MonsterSpawner.Instance.SummonFieldPlayer1 = gameObject;
			}
			if (item.Position.Equals(new Position(map.X - 1, map.Y - 1)))
			{
				MonsterSpawner.Instance.SummonFieldPlayer2 = gameObject;
			}
			spawnedTiles++;
			if (spawnedTiles % mapBuildBatchSize == 0)
			{
				await Task.Yield();
			}
		}
	}

	private void SetTileMaterial(GameObject tile, Field fieldType)
	{
		Renderer component = tile.GetComponent<Renderer>();
		component.material = fieldType switch
		{
			Field.BASE => stronghold, 
			Field.NORMAL => normal, 
			Field.OBSTACLE_SLOW => normal, 
			Field.OBSTACLE => normal, 
			Field.POWERUP => normal, 
			Field.WALL => wall, 
			_ => tile.GetComponent<Renderer>().material, 
		};
		if (fieldType == Field.EMPTY)
		{
			tile.GetComponent<Renderer>().enabled = false;
		}
	}

	public void UpdateMap(List<FieldInfo> fields)
	{
		foreach (FieldInfo field in fields)
		{
			KeyValuePair<Position, GameObject> keyValuePair = _fieldObjectMap.First((KeyValuePair<Position, GameObject> pair) => pair.Key.Equals(field.Position));
			FieldInfoWrapper component = keyValuePair.Value.GetComponent<FieldInfoWrapper>();
			if (component.fieldInfo.MonsterCard != null || component.fieldInfo.Item != null || component.fieldInfo.Entity != null)
			{
				try
				{
					if (keyValuePair.Value.transform.childCount > 0)
					{
						UnityEngine.Object.Destroy(keyValuePair.Value.transform.GetChild(0).gameObject);
					}
				}
				catch (Exception)
				{
				}
			}
			component.fieldInfo = field;
			if (field.MonsterCard != null || field.Item != null || field.Entity != null)
			{
				InstantiateFieldContent(keyValuePair.Value, field, new Vector3(-7.3f, 0.23f, -4.9f));
			}
			SetTileMaterial(keyValuePair.Value, component.fieldInfo.FieldType);
		}
	}

	private void CacheTileColor(GameObject tile)
	{
		Renderer component = tile.GetComponent<Renderer>();
		if (!(component == null) && component.enabled)
		{
			_originalTileColors[tile] = component.material.color;
		}
	}

	public void RefreshMoveHighlights(Player player)
	{
		ClearMoveHighlights();
		if (player == null)
		{
			return;
		}
		foreach (Position reachablePosition in GetReachablePositions(player))
		{
			if (_fieldObjectMap.TryGetValue(reachablePosition, out var value))
			{
				HighlightTile(value, movableFieldColor);
			}
		}
		foreach (HighlightedInteraction interactableField in GetInteractableFields(player))
		{
			if (_fieldObjectMap.TryGetValue(interactableField.Position, out var value2))
			{
				HighlightTile(value2, interactableField.HighlightColor);
			}
		}
	}

	public bool IsMoveAllowed(Player player, Position targetPosition)
	{
		if (player == null || targetPosition == null)
		{
			return false;
		}
		foreach (GameObject highlightedTile in _highlightedTiles)
		{
			Position position = highlightedTile.GetComponent<FieldInfoWrapper>().fieldInfo.Position;
			if (position != null && position.Equals(targetPosition))
			{
				return true;
			}
		}
		return false;
	}

	public void ClearMoveHighlights()
	{
		foreach (GameObject highlightedTile in _highlightedTiles)
		{
			if (!(highlightedTile == null) && _originalTileColors.TryGetValue(highlightedTile, out var value) && highlightedTile.GetComponent<FieldInfoWrapper>().fieldInfo.FieldType != Field.WALL && highlightedTile.GetComponent<FieldInfoWrapper>().fieldInfo.FieldType != Field.EMPTY)
			{
				Renderer component = highlightedTile.GetComponent<Renderer>();
				if (component != null)
				{
					component.material.color = value;
				}
			}
		}
		_highlightedTiles.Clear();
	}

	private void HighlightTile(GameObject tile, Color targetColor)
	{
		Renderer component = tile.GetComponent<Renderer>();
		if (!(component == null) && _originalTileColors.TryGetValue(tile, out var value))
		{
			component.material.color = Color.Lerp(value, targetColor, highlightBlend);
			if (!_highlightedTiles.Contains(tile))
			{
				_highlightedTiles.Add(tile);
			}
		}
	}

	private List<Position> GetReachablePositions(Player player)
	{
		List<Position> list = new List<Position>();
		if (player == null || player.Position == null)
		{
			return list;
		}
		Vector2Int[] array = new Vector2Int[4]
		{
			Vector2Int.up,
			Vector2Int.down,
			Vector2Int.left,
			Vector2Int.right
		};
		for (int i = 0; i < array.Length; i++)
		{
			Vector2Int vector2Int = array[i];
			int num = player.MaxMoveDistance;
			int num2 = player.Position.X;
			int num3 = player.Position.Y;
			if (GetFieldInfoAtPosition(num2, num3).FieldType == Field.OBSTACLE_SLOW)
			{
				num--;
			}
			while (num > 0)
			{
				int x = num2 + vector2Int.x;
				int y = num3 + vector2Int.y;
				FieldInfo fieldInfoAtPosition = GetFieldInfoAtPosition(x, y);
				if (fieldInfoAtPosition == null || fieldInfoAtPosition.FieldType == Field.WALL || fieldInfoAtPosition.FieldType == Field.EMPTY || fieldInfoAtPosition.Entity != null || fieldInfoAtPosition.Item != null || fieldInfoAtPosition.MonsterCard != null)
				{
					break;
				}
				int num4 = ((fieldInfoAtPosition.FieldType != Field.OBSTACLE_SLOW) ? 1 : 2);
				if (num < num4)
				{
					break;
				}
				list.Add(fieldInfoAtPosition.Position);
				num2 += vector2Int.x;
				num3 += vector2Int.y;
				num -= num4;
			}
		}
		return list;
	}

	private FieldInfo GetFieldInfoAtPosition(int x, int y)
	{
		foreach (KeyValuePair<Position, GameObject> item in _fieldObjectMap)
		{
			Position key = item.Key;
			if (key.X == x && key.Y == y)
			{
				return item.Value.GetComponent<FieldInfoWrapper>()?.fieldInfo;
			}
		}
		return null;
	}

	public void InitiateSummon(Player player)
	{
		foreach (HighlightedInteraction summonableField in GetSummonableFields(player))
		{
			if (_fieldObjectMap.TryGetValue(summonableField.Position, out var value))
			{
				HighlightTile(value, summonableField.HighlightColor);
			}
		}
	}

	private List<HighlightedInteraction> GetSummonableFields(Player player)
	{
		ClearMoveHighlights();
		List<HighlightedInteraction> list = new List<HighlightedInteraction>();
		if (player == null || player.Position == null)
		{
			return list;
		}
		Vector2Int[] array = new Vector2Int[4]
		{
			new Vector2Int(1, 0),
			new Vector2Int(-1, 0),
			new Vector2Int(0, 1),
			new Vector2Int(0, -1)
		};
		for (int i = 0; i < array.Length; i++)
		{
			Vector2Int vector2Int = array[i];
			FieldInfo fieldInfoAtPosition = GetFieldInfoAtPosition(player.Position.X + vector2Int.x, player.Position.Y + vector2Int.y);
			if (fieldInfoAtPosition != null && fieldInfoAtPosition.FieldType != Field.WALL && fieldInfoAtPosition.FieldType != Field.OBSTACLE && fieldInfoAtPosition.FieldType != Field.EMPTY && fieldInfoAtPosition.Item == null && fieldInfoAtPosition.MonsterCard == null && fieldInfoAtPosition.Entity == null)
			{
				list.Add(new HighlightedInteraction(fieldInfoAtPosition.Position, SummonFieldColor));
			}
		}
		return list;
	}

	private List<HighlightedInteraction> GetInteractableFields(Player player)
	{
		List<HighlightedInteraction> list = new List<HighlightedInteraction>();
		if (player == null || player.Position == null)
		{
			return list;
		}
		Vector2Int[] array = new Vector2Int[4]
		{
			new Vector2Int(1, 0),
			new Vector2Int(-1, 0),
			new Vector2Int(0, 1),
			new Vector2Int(0, -1)
		};
		for (int i = 0; i < array.Length; i++)
		{
			Vector2Int vector2Int = array[i];
			FieldInfo fieldInfoAtPosition = GetFieldInfoAtPosition(player.Position.X + vector2Int.x, player.Position.Y + vector2Int.y);
			if (fieldInfoAtPosition == null)
			{
				continue;
			}
			if (fieldInfoAtPosition.Item != null)
			{
				list.Add(new HighlightedInteraction(fieldInfoAtPosition.Position, itemInteractFieldColor));
				continue;
			}
			if (fieldInfoAtPosition.MonsterCard != null)
			{
				list.Add(new HighlightedInteraction(fieldInfoAtPosition.Position, cardInteractFieldColor));
				continue;
			}
			if (fieldInfoAtPosition.Entity != null && PlayerController.Instance.GetCurrentPlayer().Id != fieldInfoAtPosition.Entity.SummonedByPlayerId && !PlayerController.Instance.GetCurrentPlayer().HasStatus(Status.Fatigue))
			{
				list.Add(new HighlightedInteraction(fieldInfoAtPosition.Position, attackFieldColor));
				continue;
			}
			switch (fieldInfoAtPosition.FieldType)
			{
			case Field.OBSTACLE_SLOW:
				list.Add(new HighlightedInteraction(fieldInfoAtPosition.Position, ObstacleFieldColor));
				break;
			case Field.OBSTACLE:
				list.Add(new HighlightedInteraction(fieldInfoAtPosition.Position, TrapFieldColor));
				break;
			}
		}
		FieldInfo fieldInfoAtPosition2 = GetFieldInfoAtPosition(player.Position.X, player.Position.Y);
		if (fieldInfoAtPosition2 != null && fieldInfoAtPosition2.MonsterCard != null)
		{
			list.Add(new HighlightedInteraction(fieldInfoAtPosition2.Position, cardInteractFieldColor));
		}
		return list;
	}

	public void RemoveItemsAndCards()
	{
		foreach (KeyValuePair<Position, GameObject> item in _fieldObjectMap)
		{
			if (item.Value.GetComponent<FieldInfoWrapper>().fieldInfo.MonsterCard != null || item.Value.GetComponent<FieldInfoWrapper>().fieldInfo.Item != null)
			{
				item.Value.GetComponent<FieldInfoWrapper>().fieldInfo.MonsterCard = null;
				item.Value.GetComponent<FieldInfoWrapper>().fieldInfo.Item = null;
				UnityEngine.Object.Destroy(item.Value.transform.GetChild(0).gameObject);
			}
		}
	}

	public void InstantiateFieldContent(GameObject tile, FieldInfo field, Vector3 mapStartPosition)
	{
		Vector3 localPosition = new Vector3(0f, 1f, 0f);
		GameObject gameObject = new GameObject();
		if (field.Entity is Player player)
		{
			if (player.First)
			{
				if (!PlayersNames.isPlayer1Male)
				{
					gameObject = UnityEngine.Object.Instantiate(playerPrefab, tile.transform);
					portraitPlayer1.GetComponent<Image>().sprite = player1Female;
				}
				else
				{
					gameObject = UnityEngine.Object.Instantiate(playerMalePrefab, tile.transform);
					portraitPlayer1.GetComponent<Image>().sprite = player1Male;
				}
			}
			if (!player.First)
			{
				if (!PlayersNames.isPlayer2Male)
				{
					gameObject = UnityEngine.Object.Instantiate(player2Prefab, tile.transform);
					portraitPlayer2.GetComponent<Image>().sprite = player2Female;
				}
				else
				{
					gameObject = UnityEngine.Object.Instantiate(player2MalePrefab, tile.transform);
					portraitPlayer2.GetComponent<Image>().sprite = player2Male;
				}
			}
			gameObject.transform.localPosition = localPosition;
			gameObject.GetComponent<PlayerWrapper>().player = player;
			if (player.First)
			{
				gameObject.tag = "Player1";
				PlayerController.player1Prefab = gameObject;
			}
			else
			{
				gameObject.tag = "Player2";
				PlayerController.player2Prefab = gameObject;
			}
		}
		else if (field.Entity != null)
		{
			GameObject gameObject2 = UnityEngine.Object.Instantiate(monsterPrefab, tile.transform);
			gameObject2.GetComponent<MonsterWrapper>().monster = field.Entity;
			MonsterSpawner.Instance.spawnedMonsters.Add(field.Entity.Id, gameObject2.GetComponent<MonsterWrapper>());
			gameObject2.transform.localPosition = localPosition;
		}
		if (field.MonsterCard != null)
		{
			if (cardSpawner != null)
			{
				cardSpawner.InstantiateCard(field.MonsterCard.Name, tile.transform, localPosition);
				tile.GetComponent<FieldInfoWrapper>().fieldInfo = field;
			}
			else
			{
				Debug.LogWarning("CardSpawner is not assigned in MapController.");
			}
		}
		if (field.Item != null)
		{
			if (itemSpawner != null)
			{
				itemSpawner.InstantiateItem(field.Item.Name, tile.transform, localPosition);
				tile.GetComponent<FieldInfoWrapper>().fieldInfo = field;
			}
			else
			{
				Debug.LogWarning("ItemSpawner is not assigned in MapController.");
			}
		}
		if (field.FieldType == Field.OBSTACLE_SLOW)
		{
			Debug.Log("Spawned Slow");
			UnityEngine.Object.Instantiate(SlowObstacle, tile.transform).transform.localPosition = new Vector3(-0.45f, 0.5f, -0.25f);
		}
		if (field.FieldType == Field.OBSTACLE)
		{
			Debug.Log("Spawned OBSTACLE");
			UnityEngine.Object.Instantiate(Obstacle, tile.transform).transform.localPosition = new Vector3(0.34f, 0.5f, -0.3f);
		}
	}

	private async Task<GameEssentialsDTO> NewGame()
	{
		Debug.Log($"========== MapController.NewGame() - Starting game with mode: {PlayersNames.SelectedMode} ==========");
		Debug.Log("Static values - NamePl1: '" + PlayersNames.NamePl1 + "', NamePl2: '" + PlayersNames.NamePl2 + "', BotName1: '" + PlayersNames.BotName1 + "', BotName2: '" + PlayersNames.BotName2 + "', BotFilePath1: '" + PlayersNames.BotFilePath1 + "', BotFilePath2: '" + PlayersNames.BotFilePath2 + "', PvBBotName: '" + PlayersNames.PvBBotName + "', PvBBotFilePath: '" + PlayersNames.PvBBotFilePath + "'");
		string requestUrl;
		HttpResponseMessage response;
		switch (PlayersNames.SelectedMode)
		{
		case PlayersNames.GameStartMode.PlayerVsBot:
		{
			requestUrl = URL3 + "/player-vs-bot";
			string botName = (string.IsNullOrWhiteSpace(PlayersNames.PvBBotName) ? "Bot" : PlayersNames.PvBBotName);
			string text2 = (string.IsNullOrWhiteSpace(PlayersNames.PvBBotFilePath) ? "../monster-hunt-bot/go/bot_template.exe" : PlayersNames.PvBBotFilePath);
			PlayerVsBotStartRequest playerVsBotStartRequest = new PlayerVsBotStartRequest
			{
				PlayerName = (string.IsNullOrWhiteSpace(PlayersNames.NamePl1) ? "Player" : PlayersNames.NamePl1),
				BotName = botName,
				BotScriptPath = text2,
				BotLanguage = DetectBotLanguage(text2)
			};
			Debug.Log("PlayerVsBot mode - Payload: PlayerName='" + playerVsBotStartRequest.PlayerName + "', BotName='" + playerVsBotStartRequest.BotName + "', BotPath='" + playerVsBotStartRequest.BotScriptPath + "', BotLang='" + playerVsBotStartRequest.BotLanguage + "'");
			Debug.Log("PlayerVsBot mode - Sending POST to: " + requestUrl);
			string text3 = JsonConvert.SerializeObject(playerVsBotStartRequest);
			Debug.Log("PlayerVsBot mode - JSON payload: " + text3);
			StringContent content2 = new StringContent(text3, Encoding.UTF8, "application/json");
			response = await HttpClientProvider.Client.PostAsync(requestUrl, content2);
			break;
		}
		case PlayersNames.GameStartMode.BotVsBot:
		{
			requestUrl = URL3 + "/bot-vs-bot";
			BotVsBotStartRequest botVsBotStartRequest = new BotVsBotStartRequest
			{
				Bot1Name = PlayersNames.BotName1,
				Bot2Name = PlayersNames.BotName2,
				Bot1ScriptPath = PlayersNames.BotFilePath1,
				Bot2ScriptPath = PlayersNames.BotFilePath2,
				Bot1Language = DetectBotLanguage(PlayersNames.BotFilePath1),
				Bot2Language = DetectBotLanguage(PlayersNames.BotFilePath2)
			};
			Debug.Log("BotVsBot mode - Payload: Bot1Name='" + botVsBotStartRequest.Bot1Name + "', Bot1Path='" + botVsBotStartRequest.Bot1ScriptPath + "', Bot1Lang='" + botVsBotStartRequest.Bot1Language + "', Bot2Name='" + botVsBotStartRequest.Bot2Name + "', Bot2Path='" + botVsBotStartRequest.Bot2ScriptPath + "', Bot2Lang='" + botVsBotStartRequest.Bot2Language + "'");
			Debug.Log("BotVsBot mode - Sending POST to: " + requestUrl);
			string text = JsonConvert.SerializeObject(botVsBotStartRequest);
			Debug.Log("BotVsBot mode - JSON payload: " + text);
			StringContent content = new StringContent(text, Encoding.UTF8, "application/json");
			response = await HttpClientProvider.Client.PostAsync(requestUrl, content);
			break;
		}
		default:
		{
			string stringToEscape = (string.IsNullOrWhiteSpace(PlayersNames.NamePl1) ? "Warrior" : PlayersNames.NamePl1);
			string stringToEscape2 = (string.IsNullOrWhiteSpace(PlayersNames.NamePl2) ? "Paladin" : PlayersNames.NamePl2);
			requestUrl = URL3 + "/names?player1Name=" + Uri.EscapeDataString(stringToEscape) + "&player2Name=" + Uri.EscapeDataString(stringToEscape2);
			Debug.Log("PlayerVsPlayer mode - Sending GET to: " + requestUrl);
			response = await HttpClientProvider.Client.GetAsync(requestUrl);
			break;
		}
		}
		if (!response.IsSuccessStatusCode)
		{
			string arg = await response.Content.ReadAsStringAsync();
			Debug.LogError($"Game start failed ({(int)response.StatusCode}) at {requestUrl}. Body: {arg}");
			return null;
		}
		string text4 = await response.Content.ReadAsStringAsync();
		Debug.Log($"Backend response received. Status: {response.StatusCode}");
		Debug.Log("Response content: " + text4);
		JsonSerializerSettings settings = new JsonSerializerSettings
		{
			TypeNameHandling = TypeNameHandling.Auto,
			NullValueHandling = NullValueHandling.Ignore
		};
		return JsonConvert.DeserializeObject<GameEssentialsDTO>(text4, settings);
	}

	private static string DetectBotLanguage(string scriptPath)
	{
		if (string.IsNullOrWhiteSpace(scriptPath))
		{
			return "Go";
		}
		string text = Path.GetFileName(scriptPath).ToLowerInvariant();
		if (text.Contains("python") || text.Contains("py"))
		{
			return "Python";
		}
		if (text.Contains("javascript") || text.Contains("js") || text.Contains("node"))
		{
			return "JavaScript";
		}
		if (text.Contains("Java") || text.Contains("java"))
		{
			return "Java";
		}
		if (text.Contains("csharp") || text.Contains("c#") || text.Contains("dotnet"))
		{
			return "CSharp";
		}
		if (text.Contains("cpp") || text.Contains("c++") || text.Contains("cplusplus"))
		{
			return "CPlusPlus";
		}
		if (text.Contains("go") || text.Contains("golang"))
		{
			return "Go";
		}
		return Path.GetExtension(scriptPath).ToLowerInvariant() switch
		{
			".py" => "Python", 
			".java" => "Java", 
			".jar" => "Java", 
			".js" => "JavaScript", 
			".cs" => "CSharp", 
			".dll" => "CSharp", 
			".cpp" => "CPlusPlus", 
			".cc" => "CPlusPlus", 
			".cxx" => "CPlusPlus", 
			".go" => "Go", 
			".exe" => "Go", 
			"" => "Go", 
			_ => "Go", 
		};
	}
}
