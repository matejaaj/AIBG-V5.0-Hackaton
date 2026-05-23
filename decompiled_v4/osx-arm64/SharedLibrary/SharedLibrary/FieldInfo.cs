using Newtonsoft.Json;

namespace SharedLibrary;

public class FieldInfo
{
	public Position? Position;

	[JsonProperty(TypeNameHandling = TypeNameHandling.Auto)]
	public Entity? Entity;

	public Field FieldType;

	public Player? Owner;

	public Item? Item;

	public MonsterCard? MonsterCard;

	public Obstacle? Obstacle;

	public FieldInfo()
	{
		Position = null;
		Entity = null;
		FieldType = Field.NORMAL;
		Owner = null;
		Item = null;
		MonsterCard = null;
		Obstacle = null;
	}

	public FieldInfo(FieldInfo original)
	{
		FieldType = original.FieldType;
		Owner = original.Owner;
		Entity = ((original.Entity != null) ? new Entity(original.Entity) : null);
		Item = ((original.Item != null) ? new Item(original.Item) : null);
		MonsterCard = ((original.MonsterCard != null) ? new MonsterCard(original.MonsterCard) : null);
		Position = ((original.Position != null) ? new Position(original.Position.X, original.Position.Y) : null);
		Obstacle = original.Obstacle;
	}

	public FieldInfo(Position position, Field field, Entity? entity, Player? owner, Item? item, MonsterCard? monsterCard)
	{
		Position = position;
		Entity = entity;
		FieldType = field;
		Owner = owner;
		Item = item;
		MonsterCard = monsterCard;
	}

	public override string ToString()
	{
		if (Position == null)
		{
			return "No Position";
		}
		return $"({Position.X}, {Position.Y})";
	}
}
