using System;

namespace Server.Exceptions;

public class InvalidMonsterException : Exception
{
	public int MonsterId;

	public InvalidMonsterException(int monsterId, string message)
		: base(message)
	{
		MonsterId = monsterId;
	}

	public override string ToString()
	{
		return $"InvalidUserException: {Message} (Monster ID: {MonsterId})";
	}
}
