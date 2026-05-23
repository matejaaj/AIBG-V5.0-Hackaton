using System;
using System.Collections;
using SharedLibrary;
using UnityEngine;

public class AnimatorController : MonoBehaviour
{
	private Animator playerAnimator;

	private void Start()
	{
		playerAnimator = GetComponent<Animator>();
	}

	public void TriggerAttack()
	{
		if (playerAnimator != null)
		{
			playerAnimator.SetTrigger("Attack");
		}
	}

	public void TriggerPickUpCard()
	{
		if (playerAnimator != null)
		{
			playerAnimator.SetTrigger("PickUpCard");
		}
	}

	public void TriggerTakeDamage()
	{
		if (playerAnimator != null)
		{
			playerAnimator.SetTrigger("TakeDamage");
		}
	}

	public void TriggerPickUpItem()
	{
		if (playerAnimator != null)
		{
			playerAnimator.SetTrigger("PickUpItem");
		}
	}

	public void TriggerMove(Direction direction)
	{
		if (!(playerAnimator == null))
		{
			switch (direction)
			{
			case Direction.UP:
				playerAnimator.SetTrigger("MoveUp");
				break;
			case Direction.LEFT:
				playerAnimator.SetTrigger("MoveLeft");
				break;
			case Direction.RIGHT:
				playerAnimator.SetTrigger("MoveRight");
				break;
			case Direction.DOWN:
				playerAnimator.SetTrigger("MoveDown");
				break;
			default:
				throw new ArgumentOutOfRangeException("direction", direction, "This direction does not exist or is null");
			}
		}
	}

	public IEnumerator MoveToRoutine(GameObject targetField)
	{
		float duration = 2f;
		float elapsed = 0f;
		Vector3 startPos = base.transform.position;
		Vector3 endPos = targetField.transform.position;
		float num = endPos.x - startPos.x;
		float num2 = endPos.z - startPos.z;
		string animParam = ((!(Mathf.Abs(num) > Mathf.Abs(num2))) ? ((num2 > 0f) ? "MoveUp" : "MoveDown") : ((num > 0f) ? "MoveRight" : "MoveLeft"));
		if (playerAnimator != null)
		{
			playerAnimator.SetBool(animParam, value: true);
		}
		while (elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = elapsed / duration;
			float x = Mathf.Lerp(startPos.x, endPos.x, t);
			float z = Mathf.Lerp(startPos.z, endPos.z, t);
			base.transform.position = new Vector3(x, base.transform.position.y, z);
			yield return null;
		}
		playerAnimator.SetBool(animParam, value: false);
		base.transform.SetParent(targetField.transform);
	}
}
