using System;

namespace Server.Exceptions;

public class OutOfBoundsMoveException : Exception
{
	public int posX;

	public int posY;

	public OutOfBoundsMoveException(string message, int posX, int posY)
		: base(message)
	{
		posX = posX;
		posY = posY;
	}

	public override string ToString()
	{
		return $"OutOfBoundsMoveException: {Message} (Position: {posX}, {posY})";
	}
}
