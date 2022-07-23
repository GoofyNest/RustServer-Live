#define UNITY_ASSERTIONS
using System;
using ConVar;
using Facepunch;
using Network;
using ProtoBuf;
using Rust;
using Rust.UI;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;

public class TrainEngine : TrainCar, IEngineControllerUser, IEntity
{
	public enum EngineSpeeds
	{
		Rev_Hi,
		Rev_Med,
		Rev_Lo,
		Zero,
		Fwd_Lo,
		Fwd_Med,
		Fwd_Hi
	}

	private float buttonHoldTime;

	public const float HAZARD_CHECK_EVERY = 1f;

	public const float HAZARD_DIST_MAX = 325f;

	public const float HAZARD_DIST_MIN = 20f;

	public const float HAZARD_SPEED_MIN = 4.5f;

	private static readonly EngineSpeeds MaxThrottle = EngineSpeeds.Fwd_Hi;

	private static readonly EngineSpeeds MinThrottle = EngineSpeeds.Rev_Hi;

	private EngineDamageOverTime engineDamage;

	private Vector3 engineLocalOffset;

	[Header("Train Engine")]
	[SerializeField]
	private Transform leftHandLever;

	[SerializeField]
	private Transform rightHandLever;

	[SerializeField]
	private Transform leftHandGrip;

	[SerializeField]
	private Transform rightHandGrip;

	[SerializeField]
	private Canvas monitorCanvas;

	[SerializeField]
	private RustText monitorText;

	[SerializeField]
	private float engineForce = 50000f;

	[SerializeField]
	private float maxSpeed = 12f;

	[SerializeField]
	private float engineStartupTime = 1f;

	[SerializeField]
	private GameObjectRef fuelStoragePrefab;

	[SerializeField]
	private float idleFuelPerSec = 0.05f;

	[SerializeField]
	private float maxFuelPerSec = 0.15f;

	[SerializeField]
	private ProtectionProperties driverProtection;

	[SerializeField]
	private VehicleLight[] lights;

	[SerializeField]
	private ParticleSystemContainer fxLightDamage;

	[SerializeField]
	private ParticleSystemContainer fxMediumDamage;

	[SerializeField]
	private ParticleSystemContainer fxHeavyDamage;

	[SerializeField]
	private ParticleSystemContainer fxEngineTrouble;

	[SerializeField]
	private BoxCollider engineWorldCol;

	[SerializeField]
	private float engineDamageToSlow = 150f;

	[SerializeField]
	private float engineDamageTimeframe = 10f;

	[SerializeField]
	private float engineSlowedTime = 10f;

	[SerializeField]
	private float engineSlowedMaxVel = 4f;

	[SerializeField]
	private ParticleSystemContainer[] sparks;

	[FormerlySerializedAs("brakeSparkLights")]
	[SerializeField]
	private Light[] sparkLights;

	[SerializeField]
	private TrainEngineAudio trainAudio;

	public const Flags Flag_HazardAhead = Flags.Reserved6;

	public const Flags Flag_AltColor = Flags.Reserved9;

	public const Flags Flag_EngineSlowed = Flags.Reserved10;

	private VehicleEngineController<TrainEngine> engineController;

	public override bool IsEngine => true;

	protected override bool networkUpdateOnCompleteTrainChange => true;

	public bool LightsAreOn => HasFlag(Flags.Reserved5);

	public bool CloseToHazard => HasFlag(Flags.Reserved6);

	public bool EngineIsSlowed => HasFlag(Flags.Reserved10);

	public EngineSpeeds CurThrottleSetting { get; private set; } = EngineSpeeds.Zero;


	public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
	{
		using (TimeWarning.New("TrainEngine.OnRpcMessage"))
		{
			if (rpc == 1851540757 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log(string.Concat("SV_RPCMessage: ", player, " - RPC_OpenFuel "));
				}
				using (TimeWarning.New("RPC_OpenFuel"))
				{
					try
					{
						using (TimeWarning.New("Call"))
						{
							RPCMessage rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg2 = rPCMessage;
							RPC_OpenFuel(msg2);
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
						player.Kick("RPC Error in RPC_OpenFuel");
					}
				}
				return true;
			}
		}
		return base.OnRpcMessage(player, rpc, msg);
	}

	public override void ServerInit()
	{
		base.ServerInit();
		engineDamage = new EngineDamageOverTime(engineDamageToSlow, engineDamageTimeframe, OnEngineTookHeavyDamage);
		engineLocalOffset = base.transform.InverseTransformPoint(engineWorldCol.transform.position + engineWorldCol.transform.rotation * engineWorldCol.center);
	}

	protected override void OnChildAdded(BaseEntity child)
	{
		base.OnChildAdded(child);
		if (base.isServer && isSpawned)
		{
			GetFuelSystem().CheckNewChild(child);
		}
	}

	public override void VehicleFixedUpdate()
	{
		base.VehicleFixedUpdate();
		engineController.CheckEngineState();
		if (engineController.IsOn)
		{
			float fuelPerSecond = Mathf.Lerp(idleFuelPerSec, maxFuelPerSec, Mathf.Abs(GetThrottleFraction()));
			if (engineController.TickFuel(fuelPerSecond) > 0)
			{
				ClientRPC(null, "SetFuelAmount", GetFuelAmount());
			}
		}
		else if (LightsAreOn && !HasDriver())
		{
			SetFlag(Flags.Reserved5, b: false);
		}
	}

	public override void Save(SaveInfo info)
	{
		base.Save(info);
		info.msg.trainEngine = Facepunch.Pool.Get<ProtoBuf.TrainEngine>();
		info.msg.trainEngine.throttleSetting = (int)CurThrottleSetting;
		info.msg.trainEngine.fuelStorageID = GetFuelSystem().fuelStorageInstance.uid;
		info.msg.trainEngine.fuelAmount = GetFuelAmount();
		info.msg.trainEngine.numConnectedCars = completeTrain.NumTrainCars;
	}

	public override EntityFuelSystem GetFuelSystem()
	{
		return engineController.FuelSystem;
	}

	public override void LightToggle(BasePlayer player)
	{
		if (IsDriver(player))
		{
			SetFlag(Flags.Reserved5, !LightsAreOn);
		}
	}

	public override void PlayerServerInput(InputState inputState, BasePlayer player)
	{
		if (!IsDriver(player))
		{
			return;
		}
		if (engineController.IsOff)
		{
			if ((inputState.IsDown(BUTTON.FORWARD) && !inputState.WasDown(BUTTON.FORWARD)) || (inputState.IsDown(BUTTON.BACKWARD) && !inputState.WasDown(BUTTON.BACKWARD)))
			{
				engineController.TryStartEngine(player);
			}
		}
		else if (!ProcessThrottleInput(BUTTON.FORWARD, IncreaseThrottle))
		{
			ProcessThrottleInput(BUTTON.BACKWARD, DecreaseThrottle);
		}
		if (inputState.IsDown(BUTTON.LEFT))
		{
			SetTrackSelection(TrainTrackSpline.TrackSelection.Left);
		}
		else if (inputState.IsDown(BUTTON.RIGHT))
		{
			SetTrackSelection(TrainTrackSpline.TrackSelection.Right);
		}
		else
		{
			SetTrackSelection(TrainTrackSpline.TrackSelection.Default);
		}
		bool ProcessThrottleInput(BUTTON button, Action action)
		{
			if (inputState.IsDown(button))
			{
				if (!inputState.WasDown(button))
				{
					action();
					buttonHoldTime = 0f;
				}
				else
				{
					buttonHoldTime += player.clientTickInterval;
					if (buttonHoldTime > 0.55f)
					{
						action();
						buttonHoldTime = 0.4f;
					}
				}
				return true;
			}
			return false;
		}
	}

	public override void ScaleDamageForPlayer(BasePlayer player, HitInfo info)
	{
		base.ScaleDamageForPlayer(player, info);
		driverProtection.Scale(info.damageTypes);
	}

	public bool MeetsEngineRequirements()
	{
		if (!HasDriver() && CurThrottleSetting == EngineSpeeds.Zero)
		{
			return false;
		}
		if (!completeTrain.AnyPlayersOnTrain())
		{
			return vehicle.trainskeeprunning;
		}
		return true;
	}

	public void OnEngineStartFailed()
	{
	}

	public override void AttemptMount(BasePlayer player, bool doMountChecks = true)
	{
		if (CanMount(player))
		{
			base.AttemptMount(player, doMountChecks);
		}
	}

	public override float GetForces()
	{
		float num = base.GetForces();
		if (IsDead() || base.IsDestroyed)
		{
			return num;
		}
		float num2 = (engineController.IsOn ? GetThrottleFraction() : 0f);
		float value = maxSpeed * num2;
		float curTopSpeed = GetCurTopSpeed();
		value = Mathf.Clamp(value, 0f - curTopSpeed, curTopSpeed);
		float trackSpeed = GetTrackSpeed();
		if (num2 > 0f && trackSpeed < value)
		{
			num += GetCurEngineForce();
		}
		else if (num2 < 0f && trackSpeed > value)
		{
			num -= GetCurEngineForce();
		}
		return num;
	}

	public override void Hurt(HitInfo info)
	{
		if (engineDamage != null && Vector3.SqrMagnitude(engineLocalOffset - info.HitPositionLocal) < 2f)
		{
			engineDamage.TakeDamage(info.damageTypes.Total());
		}
		base.Hurt(info);
	}

	public void StopEngine()
	{
		engineController.StopEngine();
	}

	protected override Vector3 GetExplosionPos()
	{
		return engineWorldCol.transform.position + engineWorldCol.center;
	}

	private void IncreaseThrottle()
	{
		if (CurThrottleSetting != MaxThrottle)
		{
			SetThrottle(CurThrottleSetting + 1);
		}
	}

	private void DecreaseThrottle()
	{
		if (CurThrottleSetting != MinThrottle)
		{
			SetThrottle(CurThrottleSetting - 1);
		}
	}

	private void SetZeroThrottle()
	{
		SetThrottle(EngineSpeeds.Zero);
	}

	protected override void ServerFlagsChanged(Flags old, Flags next)
	{
		base.ServerFlagsChanged(old, next);
		if (next.HasFlag(Flags.On) && !old.HasFlag(Flags.On))
		{
			SetFlag(Flags.Reserved5, b: true);
			InvokeRandomized(CheckForHazards, 0f, 1f, 0.1f);
		}
		else if (!next.HasFlag(Flags.On) && old.HasFlag(Flags.On))
		{
			CancelInvoke(CheckForHazards);
			SetFlag(Flags.Reserved6, b: false);
		}
	}

	private void CheckForHazards()
	{
		float trackSpeed = GetTrackSpeed();
		if (trackSpeed > 4.5f || trackSpeed < -4.5f)
		{
			float maxHazardDist = Mathf.Lerp(40f, 325f, Mathf.Abs(trackSpeed) * 0.05f);
			SetFlag(Flags.Reserved6, base.FrontTrackSection.HasValidHazardWithin(this, base.FrontWheelSplineDist, 20f, maxHazardDist, localTrackSelection, trackSpeed, base.RearTrackSection, null));
		}
		else
		{
			SetFlag(Flags.Reserved6, b: false);
		}
	}

	private void OnEngineTookHeavyDamage()
	{
		SetFlag(Flags.Reserved10, b: true);
		Invoke(ResetEngineToNormal, engineSlowedTime);
	}

	private void ResetEngineToNormal()
	{
		SetFlag(Flags.Reserved10, b: false);
	}

	private float GetCurTopSpeed()
	{
		float num = maxSpeed * GetEnginePowerMultiplier(0.5f);
		if (EngineIsSlowed)
		{
			num = Mathf.Clamp(num, 0f - engineSlowedMaxVel, engineSlowedMaxVel);
		}
		return num;
	}

	private float GetCurEngineForce()
	{
		return engineForce * GetEnginePowerMultiplier(0.75f);
	}

	[RPC_Server]
	public void RPC_OpenFuel(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (!(player == null) && CanBeLooted(player))
		{
			GetFuelSystem().LootFuel(player);
		}
	}

	public override void InitShared()
	{
		base.InitShared();
		engineController = new VehicleEngineController<TrainEngine>(this, base.isServer, engineStartupTime, fuelStoragePrefab);
		if (base.isServer)
		{
			bool b = SeedRandom.Range(net.ID, 0, 2) == 0;
			SetFlag(Flags.Reserved9, b);
		}
	}

	public override void Load(LoadInfo info)
	{
		base.Load(info);
		if (info.msg.trainEngine != null)
		{
			engineController.FuelSystem.fuelStorageInstance.uid = info.msg.trainEngine.fuelStorageID;
			SetThrottle((EngineSpeeds)info.msg.trainEngine.throttleSetting);
		}
	}

	public override bool CanBeLooted(BasePlayer player)
	{
		if (!base.CanBeLooted(player))
		{
			return false;
		}
		if (player.isMounted)
		{
			return false;
		}
		if (platformParentTrigger != null && !PlayerIsInParentTrigger(player))
		{
			return false;
		}
		return true;
	}

	private float GetEnginePowerMultiplier(float minPercent)
	{
		if (base.healthFraction > 0.4f)
		{
			return 1f;
		}
		return Mathf.Lerp(minPercent, 1f, base.healthFraction / 0.4f);
	}

	public float GetThrottleFraction()
	{
		return CurThrottleSetting switch
		{
			EngineSpeeds.Rev_Hi => -1f, 
			EngineSpeeds.Rev_Med => -0.5f, 
			EngineSpeeds.Rev_Lo => -0.2f, 
			EngineSpeeds.Zero => 0f, 
			EngineSpeeds.Fwd_Lo => 0.2f, 
			EngineSpeeds.Fwd_Med => 0.5f, 
			EngineSpeeds.Fwd_Hi => 1f, 
			_ => 0f, 
		};
	}

	public bool IsNearDesiredSpeed(float leeway)
	{
		float num = Vector3.Dot(base.transform.forward, GetLocalVelocity());
		float num2 = maxSpeed * GetThrottleFraction();
		if (num2 < 0f)
		{
			return num - leeway <= num2;
		}
		return num + leeway >= num2;
	}

	protected override void SetTrackSelection(TrainTrackSpline.TrackSelection trackSelection)
	{
		base.SetTrackSelection(trackSelection);
	}

	private void SetThrottle(EngineSpeeds throttle)
	{
		if (CurThrottleSetting != throttle)
		{
			CurThrottleSetting = throttle;
			if (base.isServer)
			{
				ClientRPC(null, "SetThrottle", (sbyte)throttle);
			}
		}
	}

	private int GetFuelAmount()
	{
		if (base.isServer)
		{
			return engineController.FuelSystem.GetFuelAmount();
		}
		return 0;
	}

	private bool CanMount(BasePlayer player)
	{
		if (platformParentTrigger != null)
		{
			return PlayerIsInParentTrigger(player);
		}
		return true;
	}

	private bool PlayerIsInParentTrigger(BasePlayer player)
	{
		return player.GetParentEntity() == this;
	}

	void IEngineControllerUser.Invoke(Action action, float time)
	{
		Invoke(action, time);
	}

	void IEngineControllerUser.CancelInvoke(Action action)
	{
		CancelInvoke(action);
	}
}
