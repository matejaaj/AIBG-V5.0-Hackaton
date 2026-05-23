using System;
using SharedLibrary;
using UnityEngine;

namespace Utility;

[Serializable]
public class CardWrapper
{
	[SerializeField]
	public Sprite sprite;

	[SerializeField]
	public MonsterCard card;

	[SerializeField]
	public GameObject obj;
}
