using System;
using System.Collections.Generic;
using System.Linq;

namespace SharedLibrary;

public class Map
{
	public int X;

	public int Y;

	public string Name;

	public List<FieldInfo> Grid;

	public Map(int x, int y, string name, List<FieldInfo> grid)
	{
		X = x;
		Y = y;
		Name = name;
		Grid = grid;
	}

	public Map()
	{
		X = 3;
		Y = 3;
		Name = "snow";
		Grid = new List<FieldInfo>();
	}

	public Map(Map original)
	{
		X = original.X;
		Y = original.Y;
		Name = original.Name;
		Grid = original.Grid.Select((FieldInfo field) => new FieldInfo(field)).ToList();
	}

	public FieldInfo GetFieldAt(Position pos)
	{
		try
		{
			return Grid[pos.X * Y + pos.Y];
		}
		catch (Exception)
		{
			return null;
		}
	}
}
