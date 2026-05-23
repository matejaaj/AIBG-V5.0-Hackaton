using Controllers;
using SharedLibrary;
using UnityEngine;

namespace Utility;

public class MonsterWrapper : MonoBehaviour, IEntityWrapper
{
	public Entity monster;

	[SerializeField]
	private MonsterAnimationController _animatorController;

	public int Id => monster.Id;

	private void Awake()
	{
		if (_animatorController == null)
		{
			_animatorController = GetComponent<MonsterAnimationController>();
		}
		if (_animatorController == null)
		{
			_animatorController = GetComponentInChildren<MonsterAnimationController>();
		}
	}

	public void TriggerAttackAnim()
	{
		if (_animatorController == null)
		{
			Debug.LogError("MonsterWrapper: Missing MonsterAnimationController reference.");
		}
		else
		{
			_animatorController.TriggerAttack();
		}
	}

	public void EntityUpdate(Entity entity)
	{
		monster = entity;
	}

	public void TriggerTakeDamageAnim()
	{
		if (_animatorController == null)
		{
			Debug.LogError("MonsterWrapper: Missing MonsterAnimationController reference.");
		}
		else
		{
			_animatorController.TakeDamage();
		}
	}

	public void Move(Position position)
	{
		FieldInfoWrapper fieldInfoWrapper = ((base.transform.parent != null) ? base.transform.parent.GetComponent<FieldInfoWrapper>() : null);
		if (fieldInfoWrapper != null)
		{
			fieldInfoWrapper.fieldInfo.Entity = null;
			base.transform.SetParent(null);
		}
		monster.Position = position;
		GameObject fieldGameObject = MapController.Instance.GetFieldGameObject(position);
		if (!(fieldGameObject == null))
		{
			base.transform.SetParent(fieldGameObject.transform);
			switch (monster.Name)
			{
			case "Ice Cube":
				base.transform.localPosition = new Vector3(0f, 0.5f, 0f);
				break;
			case "Ice Mage":
				base.transform.localPosition = new Vector3(1.5f, 0.5f, 0.6f);
				break;
			case "Ice Warrior":
				base.transform.localPosition = new Vector3(-0.15f, 0.5f, 0.378f);
				break;
			default:
				base.transform.localPosition = new Vector3(0f, 0.5f, 0f);
				break;
			}
			FieldInfoWrapper component = fieldGameObject.GetComponent<FieldInfoWrapper>();
			if (component != null)
			{
				component.fieldInfo.Entity = monster;
			}
		}
	}
}
