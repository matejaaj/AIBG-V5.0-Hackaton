using Controllers;
using SharedLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Utility;

public class GameManager : MonoBehaviour
{
	[SerializeField]
	private GameObject endScreen;

	[SerializeField]
	private GameObject drawScreen;

	[SerializeField]
	private TextMeshProUGUI winnerTextBox;

	[SerializeField]
	private TextMeshProUGUI drawTextBox;

	[SerializeField]
	private TextMeshProUGUI statTextBoxPlayer1;

	[SerializeField]
	private TextMeshProUGUI statTextBoxPlayer2;

	[SerializeField]
	protected internal WebSocketClient _webSocketClient;

	public string _gameId;

	public static GameManager Instance { get; private set; }

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Object.Destroy(base.gameObject);
		}
		else
		{
			Instance = this;
		}
	}

	public void GameOver(Player player)
	{
		if (player == null)
		{
			drawTextBox.text = "This is a draw";
			drawScreen.SetActive(value: true);
		}
		else
		{
			winnerTextBox.text = "Player " + player.Name + " has won this round";
			endScreen.SetActive(value: true);
		}
	}

	public void NewGame()
	{
		_webSocketClient.GetMessageHandler().Reset();
		MapController.Instance.ResetAndReload();
		endScreen.SetActive(value: false);
		drawScreen.SetActive(value: false);
	}

	public void GoBackToMainMenu()
	{
		SceneManager.LoadScene("Main menu");
	}

	public IEntityWrapper GetEntityWrapper(int id)
	{
		PlayerWrapper playerWrapper = PlayerController.Instance.GetPlayerWrapper(id);
		if (playerWrapper != null)
		{
			return playerWrapper;
		}
		MonsterWrapper monster = MonsterSpawner.Instance.GetMonster(id);
		if (monster != null)
		{
			return monster;
		}
		return null;
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}
}
