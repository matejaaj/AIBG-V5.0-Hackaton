using Controllers;
using SharedLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Utility;

public class CardButton : MonoBehaviour
{
	public MonsterCard card;

	public int playerId;

	public Image sprite;

	public TextMeshProUGUI usesText;

	public TextMeshProUGUI cooldownText;

	public Button button;

	private bool locked;

	private void Start()
	{
		button = GetComponentInParent<Button>();
		sprite = GetComponentInChildren<Image>();
		TextMeshProUGUI[] componentsInChildren = GetComponentsInChildren<TextMeshProUGUI>();
		foreach (TextMeshProUGUI textMeshProUGUI in componentsInChildren)
		{
			if (textMeshProUGUI.name == "Uses")
			{
				usesText = textMeshProUGUI;
			}
			else
			{
				cooldownText = textMeshProUGUI;
			}
		}
		cooldownText.text = "";
		usesText.text = "";
		button.enabled = false;
		card = null;
		sprite.sprite = null;
		locked = false;
		button.interactable = false;
	}

	public void Load(int _playerId, MonsterCard _card, Sprite image)
	{
		playerId = _playerId;
		card = _card;
		button.interactable = true;
		button.onClick.RemoveListener(InitiateSummon);
		button.onClick.AddListener(InitiateSummon);
		button.enabled = true;
		sprite.sprite = image;
		sprite.enabled = true;
		usesText.text = card.Uses.ToString();
		if (card.OnCooldown && card.CooldownCounter > 0)
		{
			cooldownText.text = card.CooldownCounter.ToString();
		}
	}

	public void TurnOff()
	{
		cooldownText.text = "";
		usesText.text = "";
		card = null;
		button.enabled = false;
		button.interactable = false;
		sprite.enabled = false;
	}

	public void Lock()
	{
		if (button.interactable)
		{
			button.interactable = false;
			locked = true;
		}
	}

	public void Unlock()
	{
		if (locked || base.enabled)
		{
			locked = false;
			button.interactable = true;
		}
	}

	public void ReloadText(MonsterCard _card)
	{
		card = _card;
		usesText.text = card.Uses.ToString();
		Debug.Log(card.CooldownCounter);
		cooldownText.text = ((card.CooldownCounter > 0) ? card.CooldownCounter.ToString() : "");
		button.enabled = card.CooldownCounter == 0;
		button.interactable = card.CooldownCounter == 0;
	}

	private async void InitiateSummon()
	{
		_ = PlayerController.Instance.clickable;
		PlayerWrapper playerWrapper = PlayerController.Instance.GetPlayerWrapper(playerId);
		if (PlayerController.Instance.summonState)
		{
			PlayerController.Instance.summonState = false;
			PlayerController.Instance.pendingCard = null;
			MapController.Instance.InitiateSummon(playerWrapper.player);
		}
		else
		{
			PlayerController.Instance.summonState = true;
			PlayerController.Instance.pendingCard = card;
			MapController.Instance.InitiateSummon(playerWrapper.player);
		}
	}
}
