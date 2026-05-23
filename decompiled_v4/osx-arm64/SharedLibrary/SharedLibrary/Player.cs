using System.Collections.Generic;
using System.Linq;

namespace SharedLibrary;

public class Player : Entity
{
	public int Level;

	public int Xp;

	public List<Item> Inventory;

	public List<MonsterCard> Cards;

	public bool First;

	public Player()
	{
		Level = 1;
		Xp = 0;
		Inventory = new List<Item>();
		Cards = new List<MonsterCard>();
	}

	public Player(int level, int xp, List<Item> inventory, List<MonsterCard> cards, bool first)
	{
		Level = level;
		Xp = xp;
		Inventory = inventory;
		Cards = cards;
		First = first;
	}

	public Player(Player original)
		: base(original)
	{
		Level = original.Level;
		Xp = original.Xp;
		Inventory = original.Inventory.Select((Item item) => new Item(item)).ToList();
		Cards = original.Cards.Select((MonsterCard card) => new MonsterCard(card)).ToList();
		First = original.First;
	}
}
