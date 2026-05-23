using System;

namespace Server.Exceptions;

public class ItemException : Exception
{
	public int ItemId;

	public ItemException(int itemId, string message)
		: base(message)
	{
		ItemId = itemId;
	}

	public override string ToString()
	{
		return $"ItemException: {Message} (Item ID: {ItemId})";
	}
}
