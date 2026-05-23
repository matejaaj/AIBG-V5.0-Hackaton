using System;

namespace Server.Exceptions;

public class MonsterCardException : Exception
{
	public int MonsterId;

	public MonsterCardException(int monsterId, string message)
		: base(message)
	{
		MonsterId = monsterId;
	}

	public override string ToString()
	{
		return $"MonsterCardException: {Message} (Monster ID: {MonsterId})";
	}
}
