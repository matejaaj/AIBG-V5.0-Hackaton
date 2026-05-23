using System.Collections.Generic;

namespace SharedLibrary.DTOs;

public class AttackDTO
{
	public Entity attacker;

	public List<Entity> attacked;

	public bool areaOfEffect;

	public int damage;

	public AttackDTO(Entity _attacker, List<Entity> _attacked, int _damage)
	{
		attacker = _attacker;
		attacked = _attacked;
		damage = _damage;
		areaOfEffect = true;
	}

	public AttackDTO(Entity _attacker, Entity _attacked, int _damage)
	{
		attacker = _attacker;
		attacked = new List<Entity>();
		attacked.Add(_attacked);
		damage = _damage;
		areaOfEffect = false;
	}

	public AttackDTO()
	{
		attacker = null;
		attacked = null;
		areaOfEffect = false;
	}
}
