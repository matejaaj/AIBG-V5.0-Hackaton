using System;
using UnityEngine;

namespace Utility;

[Serializable]
public class DictionaryItem
{
	[SerializeField]
	public string name;

	[SerializeField]
	public ItemWrapper itemWrapper;
}
