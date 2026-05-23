using System.Collections.Generic;
using SharedLibrary;

namespace Server.Services;

public interface IMapServices
{
	Map GetMap(int id);

	List<FieldInfo> ShrinkMap(Map map, int shrinkIndex);
}
