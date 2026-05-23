using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[Serializable]
public class CustomPostProcessRendererFeature : ScriptableRendererFeature
{
	[SerializeField]
	private Shader m_bloomShader;

	[SerializeField]
	private Shader m_compositeShader;

	private CustomPostProcessPass1 m_customPass;

	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
	}

	public override void Create()
	{
		m_customPass = new CustomPostProcessPass1();
	}
}
