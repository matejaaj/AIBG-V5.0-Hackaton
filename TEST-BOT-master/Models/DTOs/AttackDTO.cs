using Newtonsoft.Json;

namespace SharedLibrary.DTOs
{
    public class AttackDTO
    {
        public Entity attacker;
        public List<Entity> attacked;
        public bool areaOfEffect;
        public int damage;

        public AttackDTO(Entity _attacker,List<Entity> _attacked, int _damage)
        {
            this.attacker = _attacker;
            this.attacked = _attacked;
            this.damage = _damage;
            areaOfEffect = true;
        }
        public AttackDTO(Entity _attacker,Entity _attacked, int _damage)
        {
            this.attacker = _attacker;
            this.attacked = new List<Entity>();
            this.attacked.Add(_attacked);
            this.damage = _damage;
            areaOfEffect = false;
        }
        public AttackDTO()
        {
            this.attacker = null;
            this.attacked = null;
            areaOfEffect = false;
        }
    }
}

