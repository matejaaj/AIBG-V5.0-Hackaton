using System;
using UnityEngine;

namespace Utility;

[Serializable]
public class DictionaryCard
{
	[SerializeField]
	public string name;

	[SerializeField]
	public CardWrapper cardWrapper;
}
