using UnityEngine;

[ExecuteInEditMode]
public class WaterInteraction : MonoBehaviour
{
	[SerializeField]
	private Texture2D texture;

	[Range(0f, 1f)]
	public float Displacement = 1f;

	[Range(0f, 1f)]
	public float Disturbance = 0.5f;

	private Transform cachedTransform;

	public Texture2D Texture
	{
		get
		{
			return texture;
		}
		set
		{
			texture = value;
			CheckRegister();
		}
	}

	public WaterDynamics.Image Image { get; private set; }

	public Vector2 Position { get; private set; } = Vector2.zero;


	public Vector2 Scale { get; private set; } = Vector2.one;


	public float Rotation { get; private set; }

	protected void OnEnable()
	{
		CheckRegister();
		UpdateTransform();
	}

	protected void OnDisable()
	{
		Unregister();
	}

	public void CheckRegister()
	{
		if (!base.enabled || texture == null)
		{
			Unregister();
		}
		else if (Image == null || Image.texture != texture)
		{
			Register();
		}
	}

	private void UpdateImage()
	{
		Image = new WaterDynamics.Image(texture);
	}

	private void Register()
	{
		UpdateImage();
		WaterDynamics.RegisterInteraction(this);
	}

	private void Unregister()
	{
		if (Image != null)
		{
			WaterDynamics.UnregisterInteraction(this);
			Image = null;
		}
	}

	public void UpdateTransform()
	{
		cachedTransform = ((cachedTransform != null) ? cachedTransform : base.transform);
		if (cachedTransform.hasChanged)
		{
			Vector3 position = cachedTransform.position;
			Vector3 lossyScale = cachedTransform.lossyScale;
			Position = new Vector2(position.x, position.z);
			Scale = new Vector2(lossyScale.x, lossyScale.z);
			Rotation = cachedTransform.rotation.eulerAngles.y;
			cachedTransform.hasChanged = false;
		}
	}
}
