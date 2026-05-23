using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Main_Menu_Scripts;

public class SwitchAnimationController : MonoBehaviour
{
	public GameObject LeftArrowPlayer1;

	public GameObject RightArrowPlayer1;

	public GameObject LeftArrowPlayer2;

	public GameObject RightArrowPlayer2;

	private List<Animator> animatorPlayer1 = new List<Animator>();

	private List<Animator> animatorPlayer2 = new List<Animator>();

	public bool isMalePlayer1;

	public bool isMalePlayer2;

	private Button LeftButtonPlayer1;

	private Button RightButtonPlayer1;

	private Button LeftButtonPlayer2;

	private Button RightButtonPlayer2;

	private void Start()
	{
		FindAnimatorsPlayer1();
		FindAnimatorsPlayer2();
		LeftButtonPlayer1 = LeftArrowPlayer1.GetComponent<Button>();
		RightButtonPlayer1 = RightArrowPlayer1.GetComponent<Button>();
		LeftButtonPlayer1.onClick.AddListener(ToggleCharacterPlayer1);
		RightButtonPlayer1.onClick.AddListener(ToggleCharacterPlayer1);
		RightButtonPlayer1.interactable = false;
		RightButtonPlayer1.image.color *= 0.5f;
		if (LeftArrowPlayer2 != null && RightArrowPlayer2 != null)
		{
			LeftButtonPlayer2 = LeftArrowPlayer2.GetComponent<Button>();
			RightButtonPlayer2 = RightArrowPlayer2.GetComponent<Button>();
			LeftButtonPlayer2.onClick.AddListener(ToggleCharacterPlayer2);
			RightButtonPlayer2.onClick.AddListener(ToggleCharacterPlayer2);
			RightButtonPlayer2.interactable = false;
			RightButtonPlayer2.image.color *= 0.5f;
		}
	}

	private void FindAnimatorsPlayer1()
	{
		GameObject[] array = GameObject.FindGameObjectsWithTag("Player1_Sprite");
		foreach (GameObject gameObject in array)
		{
			animatorPlayer1.Add(gameObject.GetComponent<Animator>());
		}
		if (animatorPlayer1.Count < 2)
		{
			Debug.LogError("Animator nije pronađen na objektu: " + base.gameObject.name);
		}
	}

	private void FindAnimatorsPlayer2()
	{
		if (!(LeftArrowPlayer2 == null) || !(RightArrowPlayer2 == null))
		{
			GameObject[] array = GameObject.FindGameObjectsWithTag("Player2_Sprite");
			foreach (GameObject gameObject in array)
			{
				animatorPlayer2.Add(gameObject.GetComponent<Animator>());
			}
			if (animatorPlayer2.Count < 2)
			{
				Debug.LogError("Animator nije pronađen na objektu: " + base.gameObject.name);
			}
		}
	}

	private void Update()
	{
	}

	public void ToggleCharacterPlayer1()
	{
		isMalePlayer1 = !isMalePlayer1;
		foreach (Animator item in animatorPlayer1)
		{
			item.SetTrigger(isMalePlayer1 ? "SlideLeft" : "SlideRight");
			if (isMalePlayer1)
			{
				RightButtonPlayer1.interactable = true;
				RightButtonPlayer1.image.color /= 0.5f;
				LeftButtonPlayer1.interactable = false;
				LeftButtonPlayer1.image.color *= 0.5f;
			}
			else
			{
				RightButtonPlayer1.interactable = false;
				RightButtonPlayer1.image.color *= 0.5f;
				LeftButtonPlayer1.interactable = true;
				LeftButtonPlayer1.image.color /= 0.5f;
			}
		}
	}

	public void ToggleCharacterPlayer2()
	{
		isMalePlayer2 = !isMalePlayer2;
		foreach (Animator item in animatorPlayer2)
		{
			item.SetTrigger(isMalePlayer2 ? "SlideLeft" : "SlideRight");
			if (isMalePlayer2)
			{
				RightButtonPlayer2.interactable = true;
				RightButtonPlayer2.image.color /= 0.5f;
				LeftButtonPlayer2.interactable = false;
				LeftButtonPlayer2.image.color *= 0.5f;
			}
			else
			{
				RightButtonPlayer2.interactable = false;
				RightButtonPlayer2.image.color *= 0.5f;
				LeftButtonPlayer2.interactable = true;
				LeftButtonPlayer2.image.color /= 0.5f;
			}
		}
	}
}
