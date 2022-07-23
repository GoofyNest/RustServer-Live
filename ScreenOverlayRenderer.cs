using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

public class ScreenOverlayRenderer : PostProcessEffectRenderer<ScreenOverlay>
{
	private Shader overlayShader;

	public override void Init()
	{
		base.Init();
		overlayShader = Shader.Find("Hidden/PostProcessing/ScreenOverlay");
	}

	public override void Render(PostProcessRenderContext context)
	{
		CommandBuffer command = context.command;
		command.BeginSample("ScreenOverlay");
		PropertySheet propertySheet = context.propertySheets.Get(overlayShader);
		propertySheet.properties.Clear();
		Vector4 value = new Vector4(1f, 0f, 0f, 1f);
		propertySheet.properties.SetVector("_UV_Transform", value);
		propertySheet.properties.SetFloat("_Intensity", base.settings.intensity);
		if ((bool)TOD_Sky.Instance)
		{
			propertySheet.properties.SetVector("_LightDir", context.camera.transform.InverseTransformDirection(TOD_Sky.Instance.LightDirection));
			propertySheet.properties.SetColor("_LightCol", TOD_Sky.Instance.LightColor * TOD_Sky.Instance.LightIntensity);
		}
		if ((bool)base.settings.texture.value)
		{
			propertySheet.properties.SetTexture("_Overlay", base.settings.texture.value);
		}
		if ((bool)base.settings.normals.value)
		{
			propertySheet.properties.SetTexture("_Normals", base.settings.normals.value);
		}
		context.command.BlitFullscreenTriangle(context.source, context.destination, propertySheet, (int)base.settings.blendMode.value);
		command.EndSample("ScreenOverlay");
	}
}
