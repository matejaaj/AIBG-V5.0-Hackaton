using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Controllers;
using Newtonsoft.Json;
using SharedLibrary;
using UnityEngine;

namespace Utility;

public class PlayerWrapper : MonoBehaviour, IEntityWrapper
{
	public Player player;

	[SerializeField]
	private AnimatorController animatorController;

	[SerializeField]
	private GameObject FrozenCubeVFX;

	[SerializeField]
	private GameObject ConfusedVFX;

	private string attackURL = "http://localhost:8080/player";

	private string moveURL = "http://localhost:8080/player/move";

	private string pickUpURL = "http://localhost:8080/player/pickup";

	private string pickUpCardURL = "http://localhost:8080/map/pickup";

	public int Id => player.Id;

	private void Start()
	{
		if (player == null)
		{
			player = new Player();
		}
	}

	public async void Attack(int defenderId)
	{
		TriggerAttackAnim();
		Debug.Log("Im attacking");
		string text = $"{attackURL}/{player.Id}/attack/{defenderId}/gameId/{GameManager.Instance._gameId}";
		Debug.Log(text);
		Debug.Log(GameManager.Instance._gameId);
		await HttpClientProvider.Client.PutAsync(text, null);
	}

	public async void TriggerAttackAnim()
	{
		animatorController.TriggerAttack();
		await Task.Delay(700);
	}

	public void HideIce()
	{
		FrozenCubeVFX.SetActive(value: false);
	}

	public void ShowConfused()
	{
		ConfusedVFX.SetActive(value: true);
	}

	public void HideConfused()
	{
		ConfusedVFX.SetActive(value: false);
	}

	public void ShowIce()
	{
		FrozenCubeVFX.SetActive(value: true);
	}

	public void EntityUpdate(Entity entity)
	{
		if (!(entity is Player player))
		{
			this.player.Health = entity.Health;
			this.player.ActiveStatuses = entity.ActiveStatuses;
		}
		else
		{
			this.player = player;
		}
		if (this.player.First)
		{
			PlayerController.Instance.setPlayer1(this.player);
			PlayerController.Instance.ReloadStatsPlayer1();
		}
		else
		{
			PlayerController.Instance.setPlayer2(this.player);
			PlayerController.Instance.ReloadStatsPlayer2();
		}
	}

	public void ReloadCards()
	{
		if (player.First)
		{
			PlayerController.Instance.ReloadCardsPlayer1();
		}
		else
		{
			PlayerController.Instance.ReloadCardsPlayer2();
		}
	}

	public async void TriggerTakeDamageAnim()
	{
		animatorController.TriggerTakeDamage();
		await Task.Delay(700);
	}

	public async void TriggerPickUpItemAnim()
	{
		animatorController.TriggerPickUpItem();
		await Task.Delay(700);
	}

	public async void TriggerPickUpCardAnim()
	{
		animatorController.TriggerPickUpCard();
		await Task.Delay(700);
	}

	public async void PickUpItem(FieldInfo fieldInfo)
	{
		string requestUri = $"{pickUpURL}/{player.Id}/gameId/{GameManager.Instance._gameId}";
		StringContent content = new StringContent(JsonConvert.SerializeObject(fieldInfo), Encoding.UTF8, "application/json");
		await HttpClientProvider.Client.PutAsync(requestUri, content);
	}

	public async void PickUpCard(FieldInfo fieldInfo)
	{
		string requestUri = $"{pickUpCardURL}/{player.Id}/gameId/{GameManager.Instance._gameId}";
		StringContent content = new StringContent(JsonConvert.SerializeObject(fieldInfo), Encoding.UTF8, "application/json");
		await HttpClientProvider.Client.PutAsync(requestUri, content);
	}

	public void Move(Position position)
	{
		FieldInfoWrapper component = base.transform.parent.GetComponent<FieldInfoWrapper>();
		if (component != null)
		{
			component.fieldInfo.Entity = null;
			base.transform.SetParent(null);
		}
		player.Position = position;
		GameObject fieldGameObject = MapController.Instance.GetFieldGameObject(position);
		if (!(fieldGameObject == null))
		{
			StartCoroutine(animatorController.MoveToRoutine(fieldGameObject));
			FieldInfoWrapper component2 = fieldGameObject.GetComponent<FieldInfoWrapper>();
			if (component2 != null)
			{
				component2.fieldInfo.Entity = player;
			}
			base.transform.localPosition = new Vector3(0f, 0.5f, 0f);
		}
	}

	public async void MovePlayer(FieldInfo fieldInfo)
	{
		MoveRequest value = new MoveRequest
		{
			playerId = Id,
			newPosition = fieldInfo.Position
		};
		using HttpClient httpClient = new HttpClient();
		StringContent content = new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
		await httpClient.PutAsync(moveURL + "/gameId/" + GameManager.Instance._gameId, content);
	}

	public async void SummonMonster(MonsterCard card, Position targetPos)
	{
		Debug.Log("Used monster card" + card.Name);
		using HttpClient httpClient = new HttpClient();
		string requestUri = $"http://localhost:8080/map/{player.Id}/summon/{card.Id}/gameId/{GameManager.Instance._gameId}";
		StringContent content = new StringContent(JsonConvert.SerializeObject(targetPos), Encoding.UTF8, "application/json");
		HttpResponseMessage httpResponseMessage = await httpClient.PutAsync(requestUri, content);
		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			await httpResponseMessage.Content.ReadAsStringAsync();
			PlayerController.Instance.ShowMovementWarning("You have alredy perfomed one mov");
		}
	}

	private void Awake()
	{
	}

	private void Update()
	{
	}
}
