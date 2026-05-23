using System.Collections.Generic;
using SharedLibrary;

namespace Server.Services;

public class MapServices : IMapServices
{
	public Map GetMap(int id)
	{
		return null;
	}

	public List<FieldInfo> ShrinkMap(Map map, int shrinkIndex)
	{
		if (shrinkIndex == 2)
		{
			return SkrinkBridge(map);
		}
		int num = 3;
		int lowerMin = shrinkIndex * num;
		int lowerMax = (shrinkIndex + 1) * num;
		int higherMin = map.X - (shrinkIndex + 1) * num;
		int higherMax = map.X - shrinkIndex * num;
		List<FieldInfo> list = map.Grid.FindAll((FieldInfo field) => (field.Position.X >= lowerMin && field.Position.X < lowerMax) || (field.Position.X >= higherMin && field.Position.X < higherMax));
		foreach (FieldInfo item in list)
		{
			item.FieldType = Field.WALL;
			item.Item = null;
			item.MonsterCard = null;
			if (item.Entity is Player)
			{
				item.Entity.Health = 0;
			}
			else
			{
				item.Entity = null;
			}
		}
		return list;
	}

	public List<FieldInfo> SkrinkBridge(Map map)
	{
		int lowerXMin = 6;
		int lowerXMax = 13;
		int higherXMin = 20;
		int higherXMax = 28;
		int leftY = 3;
		int rightY = 12;
		List<FieldInfo> list = map.Grid.FindAll((FieldInfo field) => (field.Position.X >= lowerXMin && field.Position.X < lowerXMax && (field.Position.Y <= leftY || field.Position.Y >= rightY)) || (field.Position.X >= higherXMin && field.Position.X < higherXMax && (field.Position.Y <= leftY || field.Position.Y >= rightY)));
		foreach (FieldInfo item in list)
		{
			item.FieldType = Field.WALL;
			item.Item = null;
			item.MonsterCard = null;
			if (item.Entity is Player)
			{
				item.Entity.Health = 0;
			}
			else
			{
				item.Entity = null;
			}
		}
		return list;
	}
}
