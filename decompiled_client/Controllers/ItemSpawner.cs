using UnityEngine;
using Utility;

namespace Controllers;

[CreateAssetMenu(fileName = "ItemSpawner", menuName = "Custom/ItemSpawner")]
public class ItemSpawner : ScriptableObject
{
	[SerializeField]
	private NewDict ItemDictionary;

	public void InstantiateItem(string name, Transform tile_transform, Vector3 localPosition)
	{
		ItemDictionary.ToDictionary().TryGetValue(name, out var value);
		GameObject gameObject = Object.Instantiate(value.obj, tile_transform);
		if (name.Equals("Kristal života"))
		{
			gameObject.transform.localPosition = new Vector3(0f, 0.4f, 0f);
		}
		else
		{
			gameObject.transform.localPosition = localPosition;
		}
	}

	public ItemWrapper GetItem(string name)
	{
		ItemDictionary.ToDictionary().TryGetValue(name, out var value);
		return value;
	}
}
