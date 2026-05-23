using System.Threading;

namespace Server.Utility;

public class IDGenerator
{
	public static int _currentId;

	public static int GetNextId()
	{
		return Interlocked.Increment(ref _currentId);
	}
}
