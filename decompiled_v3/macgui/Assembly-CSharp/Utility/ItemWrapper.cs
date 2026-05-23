using System;
using SharedLibrary;
using UnityEngine;

namespace Utility;

[Serializable]
public class ItemWrapper
{
	[SerializeField]
	public Sprite sprite;

	[SerializeField]
	public Item item;

	[SerializeField]
	public GameObject obj;
}
