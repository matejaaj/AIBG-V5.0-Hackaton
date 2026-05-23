using UnityEngine;
using Utility;

namespace Controllers;

[CreateAssetMenu(fileName = "CardSpawner", menuName = "Custom/CardSpawner")]
public class CardSpawner : ScriptableObject
{
	[SerializeField]
	private NewDictCards CardDictionary;

	public void InstantiateCard(string name, Transform tile_transform, Vector3 localPosition)
	{
		CardDictionary.ToDictionary().TryGetValue(name, out var value);
		Object.Instantiate(value.obj, tile_transform).transform.localPosition = localPosition;
	}

	public CardWrapper GetCard(string name)
	{
		CardDictionary.ToDictionary().TryGetValue(name, out var value);
		return value;
	}
}
