
    using Newtonsoft.Json;

    namespace SharedLibrary
    {
        [JsonObject(IsReference = true)]

        public class Entity
        {
            public int Id;
            public string Name;
            public int Health;
            public int MaxHealth;
            public int AttackPower;
            public int AttackRange;
            public int MaxMoveDistance;
            public Position Position;
            public int? SummonedByPlayerId;
            public Dictionary<Status, int> ActiveStatuses;
            public int[] xPattern;
            public int[] yPattern;

            public Entity()
            {
                Name= "test";
                Health = 100;
                MaxHealth = 100;
                AttackPower = 10;
                AttackRange = 1;
                MaxMoveDistance = 4;
                Position = new Position(0,0);
                SummonedByPlayerId = null;
                ActiveStatuses = new Dictionary<Status, int>();
                xPattern= new []{ 1, -1, 0, 0 };
                yPattern = new []{ 0, 0, 1, -1 };
            }

            public Entity(int id, string name, int health, int maxHealth, int attackPower, int attackRange, int maxMoveDistance, Position position,int[] xPattern,int[] yPattern)
            {
                Id = id;
                Name = name;
                Health = health;
                MaxHealth = maxHealth;
                AttackPower = attackPower;
                AttackRange = attackRange;
                MaxMoveDistance = maxMoveDistance;
                Position = position;
                SummonedByPlayerId = null;
                ActiveStatuses = new Dictionary<Status, int>();
                this.xPattern = xPattern;
                this.yPattern = yPattern;
            }
            public Entity(Entity original)
            {
                Id = original.Id;
                Name = original.Name;
                Health = original.Health;
                MaxHealth = original.MaxHealth;
                AttackPower = original.AttackPower;
                AttackRange = original.AttackRange;
                MaxMoveDistance = original.MaxMoveDistance;
                Position = original.Position;
                SummonedByPlayerId = original.SummonedByPlayerId;
                ActiveStatuses = new Dictionary<Status, int>();
                foreach (var originalActiveStatus in original.ActiveStatuses)
                {
                    ActiveStatuses.Add(originalActiveStatus.Key,originalActiveStatus.Value);
                }
                xPattern = original.xPattern;
                yPattern = original.yPattern;
            }
            
            public bool HasStatus(Status status)
            {
                return ActiveStatuses.ContainsKey(status);
            }
        }
    }

