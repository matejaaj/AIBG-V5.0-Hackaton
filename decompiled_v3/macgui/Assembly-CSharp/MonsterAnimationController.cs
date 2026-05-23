using UnityEngine;

public class MonsterAnimationController : MonoBehaviour
{
	private Animator monsterAnimator;

	private void Start()
	{
		monsterAnimator = GetComponent<Animator>();
	}

	public void TriggerAttack()
	{
		if (monsterAnimator != null)
		{
			monsterAnimator.SetTrigger("Attack");
		}
	}

	public void TakeDamage()
	{
		_ = monsterAnimator != null;
	}

	public void Move()
	{
		if (monsterAnimator != null)
		{
			monsterAnimator.SetTrigger("Move");
		}
	}

	private void Update()
	{
	}
}
