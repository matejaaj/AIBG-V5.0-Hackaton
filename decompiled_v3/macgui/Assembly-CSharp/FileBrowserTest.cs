using System.Collections;
using Main_Menu_Scripts;
using SimpleFileBrowser;
using UnityEngine;

public class FileBrowserTest : MonoBehaviour
{
	public bool First;

	public bool IsPlayerVsBot;

	private void Start()
	{
		FileBrowser.SetFilters(true, new FileBrowser.Filter("PowerShell", ".ps1"), new FileBrowser.Filter("Executable", ".exe"), new FileBrowser.Filter("All", "*"), new FileBrowser.Filter("Java script/Type script", ".js", ".ts"), new FileBrowser.Filter("Java", ".jar"), new FileBrowser.Filter("Python", ".py"), new FileBrowser.Filter("C#", ".dll"));
		FileBrowser.SetDefaultFilter(".ps1");
		FileBrowser.SetExcludedExtensions(".lnk", ".tmp", ".zip", ".rar");
		FileBrowser.AddQuickLink("Users", "C:\\Users");
	}

	public void ShowLoader()
	{
		Debug.Log("FileBrowserTest.ShowLoader() called - this is legacy behavior, should use PlayersNames.SelectBot1File/SelectBot2File instead");
		StartCoroutine(ShowLoadDialogCoroutine());
	}

	private IEnumerator ShowLoadDialogCoroutine()
	{
		yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, allowMultiSelection: false, null, null, "Select Bot File");
		Debug.Log(FileBrowser.Success);
		if (FileBrowser.Success)
		{
			OnFilesSelected(FileBrowser.Result);
		}
	}

	private void OnFilesSelected(string[] filePaths)
	{
		Debug.Log("FileBrowserTest.OnFilesSelected() called");
		Debug.Log($"Current game mode: {PlayersNames.SelectedMode}");
		if (PlayersNames.SelectedMode == PlayersNames.GameStartMode.PlayerVsBot)
		{
			PlayersNames.PvBBotFilePath = filePaths[0];
			Debug.Log("Player vs Bot mode detected - Stored Bot Path: " + PlayersNames.PvBBotFilePath);
		}
		else if (PlayersNames.SelectedMode == PlayersNames.GameStartMode.BotVsBot)
		{
			if (First)
			{
				PlayersNames.BotFilePath1 = filePaths[0];
				Debug.Log("Bot vs Bot mode - Stored Bot 1 Path: " + PlayersNames.BotFilePath1);
			}
			else
			{
				PlayersNames.BotFilePath2 = filePaths[0];
				Debug.Log("Bot vs Bot mode - Stored Bot 2 Path: " + PlayersNames.BotFilePath2);
			}
		}
		else
		{
			Debug.LogWarning($"Unexpected game mode: {PlayersNames.SelectedMode}");
		}
		for (int i = 0; i < filePaths.Length; i++)
		{
			Debug.Log("Legacy file browser selected: " + filePaths[i]);
		}
		Debug.Log("File selected only. Not executing file from Unity client.");
	}
}
