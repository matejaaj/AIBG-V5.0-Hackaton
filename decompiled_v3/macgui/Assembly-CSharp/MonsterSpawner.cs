using System.Collections.Generic;
using Controllers;
using SharedLibrary;
using UnityEngine;
using Utility;

public class MonsterSpawner : MonoBehaviour
{
	public GameObject SummonFieldPlayer1;

	public GameObject SummonFieldPlayer2;

	public MonsterDictionary monsters;

	public Dictionary<int, MonsterWrapper> spawnedMonsters = new Dictionary<int, MonsterWrapper>();

	public static MonsterSpawner Instance { get; private set; }

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

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void InstantiateMonster(Entity monster, GameObject parentObject)
	{
		Transform parent = parentObject.transform;
		monsters.ToDictionary().TryGetValue(monster.Name, out var value);
		if (value != null)
		{
			GameObject gameObject = Object.Instantiate(value, parent);
			MonsterWrapper component = gameObject.GetComponent<MonsterWrapper>();
			component.monster = monster;
			spawnedMonsters[monster.Id] = component;
			switch (monster.Name)
			{
			case "Ice Cube":
				gameObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);
				break;
			case "Ice Mage":
				gameObject.transform.localPosition = new Vector3(1.5f, 0.5f, 0.6f);
				break;
			case "Ice Warrior":
				gameObject.transform.localPosition = new Vector3(-0.15f, 0.5f, 0.378f);
				break;
			default:
				gameObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);
				break;
			}
		}
		else
		{
			Debug.LogError("Monster not found: " + monster.Name);
		}
	}

	public MonsterWrapper GetMonster(int monsterId)
	{
		spawnedMonsters.TryGetValue(monsterId, out var value);
		if (value != null)
		{
			return value;
		}
		Debug.Log("Monsters doesn't exist");
		return null;
	}

	private Transform GetSummonLocation()
	{
		PlayerController component = GameObject.FindWithTag("PlayerController").GetComponent<PlayerController>();
		if (component.GetCurrentPlayer().Id != component.GetPlayer1().Id)
		{
			return SummonFieldPlayer2.transform;
		}
		return SummonFieldPlayer1.transform;
	}
}
