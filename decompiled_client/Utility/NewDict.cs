using System;
using System.Collections.Generic;
using UnityEngine;

namespace Utility;

[Serializable]
public class NewDict
{
	[SerializeField]
	private DictionaryItem[] dictionaryItems;

	public Dictionary<string, ItemWrapper> ToDictionary()
	{
		Dictionary<string, ItemWrapper> dictionary = new Dictionary<string, ItemWrapper>();
		DictionaryItem[] array = dictionaryItems;
		foreach (DictionaryItem dictionaryItem in array)
		{
			dictionary.Add(dictionaryItem.name, dictionaryItem.itemWrapper);
		}
		return dictionary;
	}
}
