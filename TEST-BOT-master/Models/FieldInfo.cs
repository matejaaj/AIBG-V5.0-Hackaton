using Newtonsoft.Json;

namespace SharedLibrary
{
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
            Position = original.Position;
            Entity = original.Entity;
            FieldType = original.FieldType;
            Owner = original.Owner;
            Item = original.Item;
            MonsterCard = original.MonsterCard;
            Obstacle = original.Obstacle;
        }
    
        public FieldInfo(Position position,Field field,Entity? entity,Player? owner, Item? item,MonsterCard? monsterCard)
        {
            Position = position;
            Entity = entity;
            FieldType = field;
            Owner = owner;
            Item = item;
            MonsterCard=monsterCard;
        }

        public override string ToString()
        {
            return Position != null ? $"({Position.X}, {Position.Y})" : "No Position";
        }

    }
}

