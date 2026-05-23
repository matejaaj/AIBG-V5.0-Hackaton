using System;
using System.Collections.Generic;
using UnityEngine;

namespace Utility;

[Serializable]
public class NewDictCards
{
	[SerializeField]
	private DictionaryCard[] dictionaryCards;

	public Dictionary<string, CardWrapper> ToDictionary()
	{
		Dictionary<string, CardWrapper> dictionary = new Dictionary<string, CardWrapper>();
		DictionaryCard[] array = dictionaryCards;
		foreach (DictionaryCard dictionaryCard in array)
		{
			dictionary.Add(dictionaryCard.name, dictionaryCard.cardWrapper);
		}
		return dictionary;
	}
}
