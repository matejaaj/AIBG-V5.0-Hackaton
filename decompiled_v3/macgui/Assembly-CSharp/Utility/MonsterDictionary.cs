using System;
using System.Collections.Generic;
using UnityEngine;

namespace Utility;

[Serializable]
public class MonsterDictionary
{
	[SerializeField]
	private DictionaryMonster[] dictionaryMonsters;

	public Dictionary<string, GameObject> ToDictionary()
	{
		Dictionary<string, GameObject> dictionary = new Dictionary<string, GameObject>();
		DictionaryMonster[] array = dictionaryMonsters;
		foreach (DictionaryMonster dictionaryMonster in array)
		{
			dictionary.Add(dictionaryMonster.name, dictionaryMonster.monsterPrefab);
		}
		return dictionary;
	}
}
