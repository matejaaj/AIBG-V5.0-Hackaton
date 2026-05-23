
namespace SharedLibrary
{
    public class MonsterCard
    {
        public int Id;
        public string Name;
        public int? Uses;
        public string Effect;
        public int Power;
        public bool OnCooldown;
        public int Cooldown;
        public int CooldownCounter;
        public Entity Monster;

        public MonsterCard(MonsterCard original)
        { 
            Id= original.Id; 
            Name= original.Name; 
            Uses= original.Uses; 
            Effect = original.Effect; 
            Power = original.Power; 
            OnCooldown = original.OnCooldown; 
            Cooldown = original.Cooldown; 
            CooldownCounter = original.CooldownCounter; 
            Monster = new Entity(original.Monster);
        }

        public MonsterCard(int id, string name, int? uses, string effect, int cooldown, Entity monster)
        {
            Id = id;
            Name = name;
            Power = 1;
            Uses = uses;
            Effect = effect;
            OnCooldown = false;
            Cooldown = cooldown;
            CooldownCounter = 0;
            Monster = new Entity(monster);
        }

        public MonsterCard()
        {
        }
    }
}
