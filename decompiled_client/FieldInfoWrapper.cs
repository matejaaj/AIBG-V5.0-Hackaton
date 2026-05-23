using SharedLibrary;
using UnityEngine;

public class FieldInfoWrapper : MonoBehaviour
{
	public FieldInfo fieldInfo;

	private void Awake()
	{
		fieldInfo = new FieldInfo();
	}

	private void Start()
	{
	}
}
