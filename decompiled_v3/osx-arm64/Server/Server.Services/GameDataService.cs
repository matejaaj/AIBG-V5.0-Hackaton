using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Server.Utility;
using SharedLibrary;

namespace Server.Services;

public class GameDataService : IGameDataService
{
	private readonly Player[] BasePlayers = new Player[2]
	{
		new Player
		{
			Id = 1,
			Name = "Hero",
			Health = 100,
			AttackPower = 30,
			AttackRange = 1,
			Inventory = new List<Item>(),
			Cards = new List<MonsterCard>(),
			First = true,
			ActiveStatuses = new Dictionary<Status, int>()
		},
		new Player
		{
			Id = 2,
			Name = "Warrior",
			Health = 100,
			ActiveStatuses = new Dictionary<Status, int>()
		}
	};

	private static List<Item> Items = new List<Item>
	{
		new Item
		{
			Id = 1,
			Name = "Kristal života",
			Uses = 1,
			Effect = "Restores health by 50%",
			Range = null,
			Power = 50,
			ItemType = ItemType.Healing
		},
		new Item
		{
			Id = 2,
			Name = "Freeze scroll",
			Uses = 1,
			Effect = "Freezes the enemy player for 1 turn",
			Range = null,
			ItemType = ItemType.CrowdControl,
			Duration = 2
		},
		new Item
		{
			Id = 3,
			Name = "Dizzy scroll",
			Uses = 1,
			Effect = "Makes the enemy player become very dizzy and move to the opposite side",
			Range = null,
			ItemType = ItemType.Movement,
			Duration = 2
		}
	};

	private static List<MonsterCard> Cards = new List<MonsterCard>
	{
		new MonsterCard
		{
			Id = 201,
			Name = "Card of Ice Cubes",
			Uses = 3,
			Effect = "Summon an ice cubem monster",
			Power = 0,
			OnCooldown = false,
			Cooldown = 5,
			CooldownCounter = 0,
			Monster = new Entity
			{
				Name = "Ice cube",
				Health = 20,
				MaxHealth = 30,
				AttackPower = 20,
				AttackRange = 1,
				MaxMoveDistance = 3
			}
		},
		new MonsterCard
		{
			Id = 202,
			Name = "Card of Ice Mage",
			Uses = 2,
			Effect = "Summon an ice mage monster",
			Power = 0,
			OnCooldown = false,
			Cooldown = 7,
			CooldownCounter = 0,
			Monster = new Entity
			{
				Name = "Ice Mage",
				Health = 20,
				MaxHealth = 20,
				AttackPower = 40,
				AttackRange = 1,
				MaxMoveDistance = 2,
				xPattern = new int[4] { -3, -3, 2, 2 },
				yPattern = new int[4] { -2, 2, -2, 2 }
			}
		},
		new MonsterCard
		{
			Id = 203,
			Name = "Card of Ice Warrior",
			Uses = 2,
			Effect = "Summon an ice mage footman",
			Power = 0,
			OnCooldown = false,
			Cooldown = 7,
			CooldownCounter = 0,
			Monster = new Entity
			{
				Name = "Ice Warrior",
				Health = 60,
				MaxHealth = 60,
				AttackPower = 25,
				AttackRange = 1,
				MaxMoveDistance = 2,
				xPattern = new int[8] { 0, 0, 1, 2, 0, 0, 1, -1 },
				yPattern = new int[8] { 1, 2, 0, 0, -1, -2, 1, 0 }
			}
		}
	};

	private Map BaseMap = new Map
	{
		X = 3,
		Y = 3,
		Name = "Snow Map",
		Grid = new List<FieldInfo>
		{
			new FieldInfo(new Position
			{
				X = 0,
				Y = 0
			}, Field.BASE, null, null, null, null),
			new FieldInfo(new Position
			{
				X = 0,
				Y = 1
			}, Field.NORMAL, null, null, Items[0], null),
			new FieldInfo(new Position
			{
				X = 0,
				Y = 2
			}, Field.NORMAL, null, null, null, null),
			new FieldInfo(new Position
			{
				X = 1,
				Y = 0
			}, Field.NORMAL, null, null, null, null),
			new FieldInfo(new Position
			{
				X = 1,
				Y = 1
			}, Field.NORMAL, null, null, null, null),
			new FieldInfo(new Position
			{
				X = 1,
				Y = 2
			}, Field.NORMAL, null, null, null, null),
			new FieldInfo(new Position
			{
				X = 2,
				Y = 0
			}, Field.NORMAL, null, null, null, null),
			new FieldInfo(new Position
			{
				X = 2,
				Y = 1
			}, Field.NORMAL, null, null, Items[0], null),
			new FieldInfo(new Position
			{
				X = 2,
				Y = 2
			}, Field.BASE, null, null, null, null)
		}
	};

	public Map GetBaseMap()
	{
		return new Map(BaseMap);
	}

	public Map GetGeneratedMap(int x, int y)
	{
		Random random = new Random();
		Map map;
		using (StreamReader streamReader = new StreamReader("maps.json"))
		{
			string value = streamReader.ReadToEnd();
			map = JsonConvert.DeserializeObject<Map>(value);
		}
		SetBases(map);
		PlaceItem(Items[random.Next(1, Items.Count())], 0, 1, 5, 10);
		PlaceItem(Items[random.Next(1, Items.Count())], 4, 5, 4, 5);
		PlaceItem(Items[random.Next(1, Items.Count())], 4, 5, 10, 11);
		PlaceItem(Items[0], 14, 15, 0, 15);
		PlaceItem(Items[0], 0, 1, 0, 2);
		PlaceItem(Items[0], 0, 1, 13, 15);
		PlaceCard(Cards[random.Next(0, Cards.Count)], 13, 14, 0, 15);
		return map;
		void PlaceCard(MonsterCard card, int x1, int x2, int y1, int y2)
		{
			GenerateRandomObjects(map, random, 1, delegate(FieldInfo field, FieldInfo mirrorField)
			{
				MonsterCard monsterCard = new MonsterCard(card)
				{
					Id = random.Next(0, 1000000000)
				};
				field.MonsterCard = (mirrorField.MonsterCard = monsterCard);
			}, x1, x2, y1, y2);
		}
		void PlaceItem(Item item, int x1, int x2, int y1, int y2)
		{
			GenerateRandomObjects(map, random, 1, delegate(FieldInfo field, FieldInfo mirrorField)
			{
				Item item2 = new Item(item)
				{
					Id = random.Next(0, 1000000000)
				};
				field.Item = (mirrorField.Item = item2);
			}, x1, x2, y1, y2);
		}
	}

	public List<FieldInfo> GenerateItems(Map map, int randomItems, int randomCards, int shrinkIndex)
	{
		DeletePickups(map);
		Random random = new Random();
		switch (shrinkIndex)
		{
		case 0:
			PlaceItem(Items[random.Next(1, 2)], 4, 5, 4, 5);
			PlaceItem(Items[random.Next(1, 2)], 10, 11, 10, 11);
			PlaceItem(Items[0], 15, 15, 0, 15);
			PlaceCard(Cards[0], 13, 14, 0, 15);
			break;
		case 1:
			PlaceItem(Items[random.Next(1, 2)], 4, 5, 4, 5);
			PlaceItem(Items[random.Next(1, 2)], 8, 11, 9, 10);
			PlaceItem(Items[0], 14, 15, 0, 15);
			PlaceCard(Cards[0], 13, 14, 0, 7);
			PlaceCard(Cards[1], 13, 14, 8, 15);
			break;
		case 2:
			PlaceItem(Items[random.Next(1, 2)], 10, 11, 10, 11);
			PlaceItem(Items[0], 14, 15, 0, 15);
			PlaceCard(Cards[2], 13, 14, 0, 7);
			PlaceCard(Cards[1], 13, 14, 8, 15);
			break;
		case 3:
			PlaceItem(Items[random.Next(1, 2)], 10, 11, 10, 11);
			PlaceItem(Items[0], 14, 15, 0, 15);
			PlaceCard(Cards[1], 13, 14, 0, 7);
			PlaceCard(Cards[2], 14, 15, 8, 15);
			break;
		case 4:
			PlaceItem(Items[random.Next(1, 2)], 10, 11, 10, 11);
			PlaceItem(Items[0], 14, 15, 0, 15);
			PlaceCard(Cards[2], 7, 11, 9, 10);
			PlaceCard(Cards[2], 13, 15, 0, 15);
			break;
		}
		return map.Grid.FindAll((FieldInfo field) => field.Item != null || field.MonsterCard != null);
		void PlaceCard(MonsterCard card, int x1, int x2, int y1, int y2)
		{
			GenerateRandomObjects(map, random, 1, delegate(FieldInfo field, FieldInfo mirrorField)
			{
				MonsterCard monsterCard = new MonsterCard(card)
				{
					Id = random.Next(0, 1000000000)
				};
				field.MonsterCard = (mirrorField.MonsterCard = monsterCard);
			}, x1, x2, y1, y2);
		}
		void PlaceItem(Item item, int x1, int x2, int y1, int y2)
		{
			GenerateRandomObjects(map, random, 1, delegate(FieldInfo field, FieldInfo mirrorField)
			{
				Item item2 = new Item(item)
				{
					Id = random.Next(0, 1000000000)
				};
				field.Item = (mirrorField.Item = item2);
			}, x1, x2, y1, y2);
		}
	}

	public void DeletePickups(Map map)
	{
		List<FieldInfo> list = map.Grid.FindAll((FieldInfo field) => field.Item != null || field.MonsterCard != null);
		foreach (FieldInfo item in list)
		{
			item.Item = null;
			item.MonsterCard = null;
		}
	}

	private void GenerateRandomObjects(Map map, Random random, int itemsToPlacePairs, Action<FieldInfo, FieldInfo> placementLogic, int minX, int maxX, int minY, int maxY)
	{
		int num = 0;
		int num2 = itemsToPlacePairs * 15;
		int num3 = 0;
		while (num != itemsToPlacePairs || num2 < num3)
		{
			num3++;
			int num4 = random.Next(minX, maxX);
			int num5 = random.Next(minY, maxY);
			int x = map.X - 1 - num4;
			int y = num5;
			FieldInfo fieldAt = map.GetFieldAt(new Position(num4, num5));
			FieldInfo fieldAt2 = map.GetFieldAt(new Position(x, y));
			if (fieldAt.Item != null)
			{
				fieldAt2.Item.Id = random.Next(0, 1000000000);
			}
			else if (fieldAt.MonsterCard != null)
			{
				fieldAt2.MonsterCard.Id = random.Next(0, 1000000000);
			}
			if (fieldAt.FieldType == Field.NORMAL && fieldAt.Item == null && fieldAt.Entity == null && fieldAt.MonsterCard == null && fieldAt2.FieldType == Field.NORMAL && fieldAt2.Item == null && fieldAt2.Entity == null && fieldAt2.MonsterCard == null)
			{
				placementLogic(fieldAt, fieldAt2);
				num++;
			}
		}
	}

	public void SetBases(Map map)
	{
		if (map != null)
		{
			map.GetFieldAt(new Position(3, 7)).FieldType = Field.BASE;
			map.GetFieldAt(new Position(28, 7)).FieldType = Field.BASE;
		}
	}

	public Map GenerateEmptyMap(int x, int y)
	{
		List<FieldInfo> list = new List<FieldInfo>();
		for (int i = 0; i < x; i++)
		{
			for (int j = 0; j < y; j++)
			{
				FieldInfo item = new FieldInfo(new Position(i, j), Field.NORMAL, null, null, null, null);
				list.Add(item);
			}
		}
		return new Map(x, y, "Generated Map", list);
	}

	public Player GetBasePlayer(string? name)
	{
		Player player = new Player(BasePlayers[0]);
		player.Id = new Random().Next(1, 1000000000);
		if (!string.IsNullOrEmpty(name))
		{
			player.Name = name;
		}
		return player;
	}

	public Dictionary<int, Player> SetBasePlayers(string? Player1name, string? Player2name)
	{
		Dictionary<int, Player> dictionary = new Dictionary<int, Player>();
		Player basePlayer = GetBasePlayer(Player1name);
		basePlayer.Id = IDGenerator.GetNextId();
		basePlayer.First = true;
		dictionary.Add(basePlayer.Id, basePlayer);
		Player basePlayer2 = GetBasePlayer(Player2name);
		basePlayer2.Id = IDGenerator.GetNextId();
		basePlayer2.First = false;
		dictionary.Add(basePlayer2.Id, basePlayer2);
		return dictionary;
	}

	public void setPlayersOnMap(Dictionary<int, Player> players, Map map)
	{
		Position position = new Position(3, 7);
		Position position2 = new Position(28, 7);
		foreach (KeyValuePair<int, Player> player in players)
		{
			if (player.Value.First)
			{
				FieldInfo fieldAt = map.GetFieldAt(position);
				player.Value.Position = position;
				fieldAt.Entity = player.Value;
			}
			else
			{
				FieldInfo fieldAt2 = map.GetFieldAt(position2);
				player.Value.Position = position2;
				fieldAt2.Entity = player.Value;
			}
		}
	}
}
