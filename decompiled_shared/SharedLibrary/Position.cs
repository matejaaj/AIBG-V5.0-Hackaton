using System;

namespace SharedLibrary;

public class Position
{
	public int X;

	public int Y;

	public Position()
	{
		X = 0;
		Y = 0;
	}

	public Position(int x, int y)
	{
		X = x;
		Y = y;
	}

	public override bool Equals(object obj)
	{
		if (!(obj is Position position))
		{
			return false;
		}
		return X == position.X && Y == position.Y;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(X, Y);
	}
}
