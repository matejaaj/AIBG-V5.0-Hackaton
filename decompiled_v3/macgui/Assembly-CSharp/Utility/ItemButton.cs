using System.Net.Http;
using Controllers;
using SharedLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Utility;

public class ItemButton : MonoBehaviour
{
	public Item item;

	public int playerId;

	public Image sprite;

	public TextMeshProUGUI text;

	public Button button;

	public bool locked;

	private void Start()
	{
		button = GetComponentInParent<Button>();
		sprite = GetComponentInChildren<Image>();
		text = GetComponentInChildren<TextMeshProUGUI>();
		text.text = "";
		button.enabled = false;
		button.interactable = false;
		sprite.sprite = null;
	}

	public void Load(int _playerId, Item _item, Sprite image)
	{
		playerId = _playerId;
		item = _item;
		button.interactable = true;
		button.enabled = true;
		button.onClick.AddListener(UseItem);
		sprite.sprite = image;
		sprite.enabled = true;
		text.text = item.Uses.ToString();
	}

	public void TurnOff()
	{
		text.text = "";
		button.interactable = false;
		button.enabled = false;
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

	public void ReloadText(Item _item)
	{
		item = _item;
		text.text = item.Uses.ToString();
	}

	private async void UseItem()
	{
		if (PlayerController.Instance.clickable)
		{
			PlayerController.Instance.clickable = false;
			Debug.Log("Used item" + item.Name);
			string requestUri = $"http://localhost:8080/player/{playerId}/use-item/{item.Id}/gameId/{GameManager.Instance._gameId}";
			await HttpClientProvider.Client.PutAsync(requestUri, null);
		}
	}
}
