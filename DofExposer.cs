using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

[ExecuteInEditMode]
public class DofExposer : SingletonComponent<DofExposer>
{
	public PostProcessVolume PostVolume;

	public bool DofEnabled;

	public float FocalLength = 15.24f;

	public float Blur = 2f;

	public float FocalAperture = 13.16f;

	public bool debug;
}
