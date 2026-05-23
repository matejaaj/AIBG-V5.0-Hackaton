using System;

namespace Server.Exceptions;

public class InvalidPlayerException : Exception
{
	public int PlayerId;

	public InvalidPlayerException(int playerId, string message)
		: base(message)
	{
		PlayerId = playerId;
	}

	public override string ToString()
	{
		return $"InvalidUserException: {Message} (Player ID: {PlayerId})";
	}
}
