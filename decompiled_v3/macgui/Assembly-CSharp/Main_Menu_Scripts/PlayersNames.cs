using System.Collections;
using System.Collections.Generic;
using SimpleFileBrowser;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Main_Menu_Scripts;

public class PlayersNames : MonoBehaviour
{
	public enum GameStartMode
	{
		PlayerVsPlayer,
		PlayerVsBot,
		BotVsBot
	}

	public TMP_InputField Player1Name;

	public TMP_InputField Player2Name;

	public TMP_InputField Bot1Name;

	public TMP_InputField Bot2Name;

	public TMP_InputField Bot1FilePath;

	public TMP_InputField Bot2FilePath;

	[Header("Player vs Bot Fields")]
	public TMP_InputField PlayerVsBotBotName;

	public TMP_InputField PlayerVsBotBotFilePath;

	[Header("Start Buttons")]
	[SerializeField]
	private List<Button> StartButtons = new List<Button>();

	[Header("UI Groups")]
	public GameObject Player1NameGroup;

	public GameObject Player2NameGroup;

	public GameObject BotVsBotGroup;

	public GameObject BotVsBotFileGroup;

	public GameObject PlayerVsBotBotGroup;

	public static string NamePl1 = "";

	public static string NamePl2 = "";

	public static string BotName1 = "";

	public static string BotName2 = "";

	public static string BotFilePath1 = "";

	public static string BotFilePath2 = "";

	public static string PvBBotName = "";

	public static string PvBBotFilePath = "";

	public static GameStartMode SelectedMode = GameStartMode.PlayerVsPlayer;

	private readonly List<Button> _boundStartButtons = new List<Button>();

	public static bool isPlayer1Male = false;

	public static bool isPlayer2Male = false;

	private void Start()
	{
		Debug.Log($"PlayersNames.Start() - Initial mode: {SelectedMode}");
		BindStartButtons();
		RefreshVisibleGroups();
	}

	private void OnDestroy()
	{
		foreach (Button boundStartButton in _boundStartButtons)
		{
			if (boundStartButton != null)
			{
				boundStartButton.onClick.RemoveListener(NewGame);
			}
		}
		_boundStartButtons.Clear();
	}

	public void SetPlayerVsPlayerMode()
	{
		Debug.Log(">>> Mode changed to: PlayerVsPlayer");
		SelectedMode = GameStartMode.PlayerVsPlayer;
		RefreshVisibleGroups();
	}

	public void SetPlayerVsBotMode()
	{
		Debug.Log(">>> Mode changed to: PlayerVsBot");
		SelectedMode = GameStartMode.PlayerVsBot;
		RefreshVisibleGroups();
	}

	public void SetBotVsBotMode()
	{
		Debug.Log(">>> Mode changed to: BotVsBot");
		SelectedMode = GameStartMode.BotVsBot;
		RefreshVisibleGroups();
	}

	public void NewGame()
	{
		Debug.Log($"=== NewGame() called. Current mode: {SelectedMode} ===");
		if (!ValidateAndStoreInputs())
		{
			Debug.LogWarning($"Cannot start game. Mode: {SelectedMode}. Required inputs are missing.");
			return;
		}
		if (SelectedMode == GameStartMode.BotVsBot)
		{
			Debug.Log("BotVsBot ready. Bot1: " + BotName1 + ", Path1: " + BotFilePath1 + " | Bot2: " + BotName2 + ", Path2: " + BotFilePath2);
		}
		else if (SelectedMode == GameStartMode.PlayerVsBot)
		{
			Debug.Log("PlayerVsBot ready. Player: " + NamePl1);
		}
		else
		{
			Debug.Log("PlayerVsPlayer ready. Player1: " + NamePl1 + ", Player2: " + NamePl2);
		}
		Debug.Log($"Loading Map1 scene with mode: {SelectedMode}");
		SceneManager.LoadScene("Map1");
	}

	private bool ValidateAndStoreInputs()
	{
		Debug.Log($"ValidateAndStoreInputs() - Mode: {SelectedMode}");
		Debug.Log("Preserved static fields - BotFilePath1: '" + BotFilePath1 + "', BotFilePath2: '" + BotFilePath2 + "', PvBBotFilePath: '" + PvBBotFilePath + "'");
		switch (SelectedMode)
		{
		case GameStartMode.PlayerVsPlayer:
		{
			string text8 = ((Player1Name != null) ? Player1Name.text.Trim() : "");
			string text9 = ((Player2Name != null) ? Player2Name.text.Trim() : "");
			Debug.Log($"PvP validation - Player1Name field null: {Player1Name == null}, value: '{text8}' | Player2Name field null: {Player2Name == null}, value: '{text9}'");
			if (string.IsNullOrEmpty(text8) || string.IsNullOrEmpty(text9))
			{
				Debug.LogWarning("PvP validation failed - missing player names");
				return false;
			}
			NamePl1 = text8;
			NamePl2 = text9;
			Debug.Log("PvP validation passed. Stored: " + NamePl1 + ", " + NamePl2);
			GameObject obj = GameObject.FindGameObjectWithTag("SpriteController");
			isPlayer1Male = obj.GetComponent<SwitchAnimationController>().isMalePlayer1;
			isPlayer2Male = obj.GetComponent<SwitchAnimationController>().isMalePlayer2;
			return true;
		}
		case GameStartMode.PlayerVsBot:
		{
			string text5 = ((Player1Name != null) ? Player1Name.text.Trim() : "");
			string text6 = ((PlayerVsBotBotName != null) ? PlayerVsBotBotName.text.Trim() : "");
			string text7 = ((!string.IsNullOrEmpty(PvBBotFilePath)) ? PvBBotFilePath : ((PlayerVsBotBotFilePath != null) ? PlayerVsBotBotFilePath.text.Trim() : ""));
			Debug.Log("PvB validation - Static bot path: '" + PvBBotFilePath + "', UI bot path: '" + ((PlayerVsBotBotFilePath != null) ? PlayerVsBotBotFilePath.text : "null") + "'");
			Debug.Log("PvB validation - Using: player='" + text5 + "', botName='" + text6 + "', botPath='" + text7 + "'");
			if (string.IsNullOrEmpty(text5))
			{
				Debug.LogWarning("PvB validation failed - missing player name");
				return false;
			}
			if (string.IsNullOrEmpty(text6))
			{
				text6 = "Bot";
				Debug.Log("Bot name empty - using default: Bot");
			}
			if (string.IsNullOrEmpty(text7))
			{
				text7 = "../monster-hunt-bot/go/bot_template.exe";
				Debug.Log("Bot path empty - using default: ../monster-hunt-bot/go/bot_template.exe");
			}
			isPlayer1Male = GameObject.FindGameObjectWithTag("SpriteController").GetComponent<SwitchAnimationController>().isMalePlayer1;
			isPlayer2Male = false;
			NamePl1 = text5;
			PvBBotName = text6;
			PvBBotFilePath = text7;
			Debug.Log("PvB validation passed. Stored - Player: '" + NamePl1 + "', Bot: '" + PvBBotName + "', Path: '" + PvBBotFilePath + "'");
			return true;
		}
		case GameStartMode.BotVsBot:
		{
			string text = ((Bot1Name != null) ? Bot1Name.text.Trim() : "");
			string text2 = ((Bot2Name != null) ? Bot2Name.text.Trim() : "");
			string text3 = ((!string.IsNullOrEmpty(BotFilePath1)) ? BotFilePath1 : ((Bot1FilePath != null) ? Bot1FilePath.text.Trim() : ""));
			string text4 = ((!string.IsNullOrEmpty(BotFilePath2)) ? BotFilePath2 : ((Bot2FilePath != null) ? Bot2FilePath.text.Trim() : ""));
			Debug.Log("BvB validation - Bot1 static path: '" + BotFilePath1 + "', UI path: '" + ((Bot1FilePath != null) ? Bot1FilePath.text : "null") + "'");
			Debug.Log("BvB validation - Bot2 static path: '" + BotFilePath2 + "', UI path: '" + ((Bot2FilePath != null) ? Bot2FilePath.text : "null") + "'");
			Debug.Log("BvB validation - Using paths: b1Path='" + text3 + "', b2Path='" + text4 + "'");
			isPlayer1Male = false;
			isPlayer2Male = false;
			if (string.IsNullOrEmpty(text))
			{
				text = "Bot A";
				Debug.Log("Bot1 name empty - using default: Bot A");
			}
			if (string.IsNullOrEmpty(text2))
			{
				text2 = "Bot B";
				Debug.Log("Bot2 name empty - using default: Bot B");
			}
			if (string.IsNullOrEmpty(text3))
			{
				text3 = "../monster-hunt-bot/javascript/bot_template.js";
				Debug.Log("Bot1 path empty - using default: ../monster-hunt-bot/javascript/bot_template.js");
			}
			if (string.IsNullOrEmpty(text4))
			{
				text4 = "../monster-hunt-bot/python/bot_template.py";
				Debug.Log("Bot2 path empty - using default: ../monster-hunt-bot/python/bot_template.py");
			}
			BotName1 = text;
			BotName2 = text2;
			BotFilePath1 = text3;
			BotFilePath2 = text4;
			Debug.Log("BvB validation passed. Stored - Bot1: '" + BotName1 + "', Path1: '" + BotFilePath1 + "' | Bot2: '" + BotName2 + "', Path2: '" + BotFilePath2 + "'");
			return true;
		}
		default:
			return false;
		}
	}

	private void RefreshVisibleGroups()
	{
		Debug.Log($"RefreshVisibleGroups() - Current mode: {SelectedMode}");
		if (Player1NameGroup != null)
		{
			Player1NameGroup.SetActive(SelectedMode == GameStartMode.PlayerVsPlayer || SelectedMode == GameStartMode.PlayerVsBot);
		}
		if (Player2NameGroup != null)
		{
			Player2NameGroup.SetActive(SelectedMode == GameStartMode.PlayerVsPlayer);
		}
		if (BotVsBotGroup != null)
		{
			BotVsBotGroup.SetActive(SelectedMode == GameStartMode.BotVsBot);
		}
		if (BotVsBotFileGroup != null)
		{
			BotVsBotFileGroup.SetActive(SelectedMode == GameStartMode.BotVsBot);
		}
		if (PlayerVsBotBotGroup != null)
		{
			PlayerVsBotBotGroup.SetActive(SelectedMode == GameStartMode.PlayerVsBot);
		}
	}

	private void BindStartButtons()
	{
		_boundStartButtons.Clear();
		foreach (Button startButton in StartButtons)
		{
			TryBindButton(startButton);
		}
		if (_boundStartButtons.Count == 0)
		{
			TryBindButton(GetComponentInParent<Button>());
		}
	}

	private void TryBindButton(Button button)
	{
		if (!(button == null))
		{
			button.onClick.RemoveListener(NewGame);
			button.onClick.AddListener(NewGame);
			if (!_boundStartButtons.Contains(button))
			{
				_boundStartButtons.Add(button);
			}
		}
	}

	public void SelectBot1File()
	{
		Debug.Log("SelectBot1File() called - starting file browser coroutine");
		StartCoroutine(ShowBot1FileDialog());
	}

	public void SelectBot2File()
	{
		Debug.Log("SelectBot2File() called - starting file browser coroutine");
		StartCoroutine(ShowBot2FileDialog());
	}

	private IEnumerator ShowBot1FileDialog()
	{
		Debug.Log("ShowBot1FileDialog coroutine started");
		Debug.Log("Current BotFilePath1 static field value BEFORE dialog: '" + BotFilePath1 + "'");
		FileBrowser.SetFilters(true, new FileBrowser.Filter("Python", ".py"), new FileBrowser.Filter("JavaScript", ".js"), new FileBrowser.Filter("Java", ".java", ".jar"), new FileBrowser.Filter("C#", ".cs", ".dll"), new FileBrowser.Filter("C++", ".cpp", ".cc", ".cxx"), new FileBrowser.Filter("Go", ".go"), new FileBrowser.Filter("Executable", ".exe"), new FileBrowser.Filter("All", "*"));
		FileBrowser.SetDefaultFilter(".exe");
		Debug.Log("Opening file browser dialog...");
		yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, allowMultiSelection: false, null, null, "Select Bot 1 Script");
		Debug.Log($"File browser returned. Success: {FileBrowser.Success}");
		if (FileBrowser.Success)
		{
			if (FileBrowser.Result != null && FileBrowser.Result.Length != 0)
			{
				string text = FileBrowser.Result[0];
				Debug.Log("Selected path from browser: '" + text + "'");
				BotFilePath1 = text;
				Debug.Log("Set BotFilePath1 static field to: '" + BotFilePath1 + "'");
				if (Bot1FilePath != null)
				{
					Bot1FilePath.text = text;
					Debug.Log("Also updated UI field to: '" + Bot1FilePath.text + "'");
				}
				else
				{
					Debug.LogWarning("Bot1FilePath UI component is null - only static field updated");
				}
			}
			else
			{
				Debug.LogError("FileBrowser.Success is true but Result is null or empty!");
			}
		}
		else
		{
			Debug.LogWarning("Bot 1 file selection was canceled or failed.");
		}
		Debug.Log("ShowBot1FileDialog coroutine finished. Final BotFilePath1: '" + BotFilePath1 + "'");
	}

	private IEnumerator ShowBot2FileDialog()
	{
		Debug.Log("ShowBot2FileDialog coroutine started");
		Debug.Log("Current BotFilePath2 static field value BEFORE dialog: '" + BotFilePath2 + "'");
		FileBrowser.SetFilters(true, new FileBrowser.Filter("Python", ".py"), new FileBrowser.Filter("JavaScript", ".js"), new FileBrowser.Filter("Java", ".java", ".jar"), new FileBrowser.Filter("C#", ".cs", ".dll"), new FileBrowser.Filter("C++", ".cpp", ".cc", ".cxx"), new FileBrowser.Filter("Go", ".go"), new FileBrowser.Filter("Executable", ".exe"), new FileBrowser.Filter("All", "*"));
		FileBrowser.SetDefaultFilter(".exe");
		Debug.Log("Opening file browser dialog...");
		yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, allowMultiSelection: false, null, null, "Select Bot 2 Script");
		Debug.Log($"File browser returned. Success: {FileBrowser.Success}");
		if (FileBrowser.Success)
		{
			if (FileBrowser.Result != null && FileBrowser.Result.Length != 0)
			{
				string text = FileBrowser.Result[0];
				Debug.Log("Selected path from browser: '" + text + "'");
				BotFilePath2 = text;
				Debug.Log("Set BotFilePath2 static field to: '" + BotFilePath2 + "'");
				if (Bot2FilePath != null)
				{
					Bot2FilePath.text = text;
					Debug.Log("Also updated UI field to: '" + Bot2FilePath.text + "'");
				}
				else
				{
					Debug.LogWarning("Bot2FilePath UI component is null - only static field updated");
				}
			}
			else
			{
				Debug.LogError("FileBrowser.Success is true but Result is null or empty!");
			}
		}
		else
		{
			Debug.LogWarning("Bot 2 file selection was canceled or failed.");
		}
		Debug.Log("ShowBot2FileDialog coroutine finished. Final BotFilePath2: '" + BotFilePath2 + "'");
	}

	public void SelectPlayerVsBotBotFile()
	{
		StartCoroutine(ShowPlayerVsBotBotFileDialog());
	}

	private IEnumerator ShowPlayerVsBotBotFileDialog()
	{
		FileBrowser.SetFilters(true, new FileBrowser.Filter("Python", ".py"), new FileBrowser.Filter("JavaScript", ".js"), new FileBrowser.Filter("Java", ".java", ".jar"), new FileBrowser.Filter("C#", ".cs", ".dll"), new FileBrowser.Filter("C++", ".cpp", ".cc", ".cxx"), new FileBrowser.Filter("Go", ".go"), new FileBrowser.Filter("Executable", ".exe"), new FileBrowser.Filter("All", "*"));
		FileBrowser.SetDefaultFilter(".exe");
		yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, allowMultiSelection: false, null, null, "Select Bot Script");
		if (FileBrowser.Success)
		{
			string text = (PvBBotFilePath = FileBrowser.Result[0]);
			if (PlayerVsBotBotFilePath != null)
			{
				PlayerVsBotBotFilePath.text = text;
			}
			Debug.Log("Player vs Bot - Bot file selected and stored: " + text);
		}
		else
		{
			Debug.LogWarning("Player vs Bot - Bot file selection was canceled or failed.");
		}
	}
}
