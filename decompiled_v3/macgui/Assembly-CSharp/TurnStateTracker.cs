using Controllers;
using SharedLibrary;
using TMPro;
using UnityEngine;

public class TurnStateTracker : MonoBehaviour
{
	public TurnState turnState;

	public Material mat;

	[SerializeField]
	private TextMeshProUGUI turnStateText;

	[SerializeField]
	private TextMeshProUGUI turnCounter;

	public int currentTurn;

	public int maxTurn = 30;

	private void Start()
	{
		turnCounter.text = currentTurn + "/" + maxTurn;
		turnState = TurnState.Start;
	}

	private void Update()
	{
	}

	public void UpdateTurn(int turnNumber)
	{
		currentTurn = turnNumber;
		turnCounter.text = currentTurn + "/" + maxTurn;
	}

	public void SetMaxTurn(int turnNumber)
	{
		maxTurn = turnNumber;
	}

	public void ChangeTurn(TurnState state)
	{
		turnState = state;
		switch (turnState)
		{
		case TurnState.Player1Turn:
			if (GameObject.FindGameObjectWithTag("Player1") == null)
			{
				Debug.Log("no player");
			}
			turnStateText.text = "Player 1 turn";
			Debug.Log("Player 1 turn");
			PlayerController.Instance.RefreshMoveHighlights();
			break;
		case TurnState.Player2Turn:
			if (GameObject.FindGameObjectWithTag("Player2") == null)
			{
				Debug.Log("no player");
			}
			turnStateText.text = "Player 2 turn";
			Debug.Log("Player 2 turn");
			PlayerController.Instance.RefreshMoveHighlights();
			break;
		case TurnState.MonsterTurn:
			turnStateText.text = "Monster Turn";
			break;
		}
	}
}
