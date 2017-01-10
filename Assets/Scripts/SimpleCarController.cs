//#define USE_XYZ
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// 发动机状态
enum RunStage
{
	Stop = 0,  // 停车
	Init = 1,  // 启动
	Run  = 2,  // 运行
	Down = 3,  // 停止
}

[System.Serializable]
public class AxleInfo
{
	public WheelCollider leftWheel;
	public WheelCollider rightWheel;
	public bool motor;
	public bool steering;
}

public class SimpleCarController : MonoBehaviour
{
	#region Data

	private Rigidbody mRig;
	private RunStage  mStage;
	private GearPosition eGear;
	private InputCommand mLastCmd;
	private int nGearNo;
	private readonly float mDownforce = 1000;
	private readonly float mIdleTorque= 1000;
	private float throttle, motor, braker, hbraker, steering;

	private UIPC mUIPC;
    private float stiffness = 1;       // 摩擦
    private float N1 = 960, N2 = 2500; // 转速
    private float L0 = 480, L1 = 1.5f; // 油量
    private float dL1 = 5e-5f, dL2 = 5e-4f;   // 油耗
    private float T0 = 25, T1 = 90, T2 = 125; // 温度

	#endregion

	#region Property

	public List<AxleInfo> axleInfos;
	public List<float> gearsNoVels;

	/// <summary> 配置数据 </summary>
	public ConfigInfo ConfigData
	{
		get { return UIPC.cfgData; }
	}

	/// <summary> 最大速度(m/s) </summary>
	public float MaxVelocity
	{
		get { return ConfigData.maxVelocity/3.6f; }
	}

	/// <summary> 最大驱动扭矩 (N.m) </summary>
	public float MaxMotorTorque
	{
		get { return ConfigData.maxMotorTorque; }
		//set { UIPC.cfgData.maxMotorTorque = value; }
	}

	/// <summary> 最大刹车扭矩 (N.m) </summary>
	public float MaxBrakeTorque
	{
		get { return ConfigData.maxBrakeTorque; }
		//set { UIPC.cfgData.maxBrakeTorque = value; }
	}

	/// <summary> 最大转向偏转 (degree) </summary>
	public float MaxSteeringAngle
	{
		get { return ConfigData.maxSteeringAngle; }
		//set { UIPC.cfgData.maxSteeringAngle = value; }
	}

	/// <summary> 自动变档 </summary>
	public bool IsAutoGear
	{
		get
		{
			if (ConfigData.autoGear && eGear == GearPosition.D)
				return true;
			else
				return false;
		}
	}

	/// <summary> 变速器档位 ==> nGear </summary>
	public GearPosition GearNo
	{
		get {
			return eGear;
		}
		set {
			eGear = value;

			if (IsAutoGear)
				AutoChangeGear(); // 自动设置档位
			else if (eGear > GearPosition.N && eGear < GearPosition.D)
				nGearNo = (int)eGear; // 手动设置档位
		}
	}

	#endregion

	#region 初始化

	void Start()
	{
		// 构造初始化
		InitializeOnce();
		// 输出初始化
		InitOutData();
		// HACK: 减速测试
		//mRig.velocity = new Vector3(0,0,16.667f);
	}

	void LoadConfigData()
	{
		mUIPC.UpdateConfigData();
		mUIPC.UpdateInData();
		mLastCmd = UIPC.inData.cmd;
#if USE_XYZ
		// Foreword ==> X, Up ==> Y, Right ==> Z
		// X ==> zb, Y ==> yb, Z ==> xb; wz(r) ==> -wxb, wy(q) ==> -wyb,  wx(p)  ==> -wzb
		mRig.position = new Vector3(ConfigData.z, ConfigData.y, ConfigData.x);
		mRig.rotation = Quaternion.Euler(-ConfigData.r, -ConfigData.q, -ConfigData.p);
#else   // Foreword ==> Z, Up ==> Y, Right ==> X
		mRig.position = new Vector3(ConfigData.x, ConfigData.y, ConfigData.z);
		mRig.rotation = Quaternion.Euler(ConfigData.p, ConfigData.q, ConfigData.r);
#endif
		mRig.velocity = Vector3.zero;
		// FIXME: 参数设置
		mRig.centerOfMass = new Vector3 (ConfigData.centerX, ConfigData.centerY, ConfigData.centerZ);
		mRig.mass = ConfigData.carWeight;
        // 季节影响
        switch (ConfigData.season)
        {
            case ENVSeason.Spring:
                T0 = 15;
                break;
            case ENVSeason.Summary:
                T0 = 25;
                break;
            case ENVSeason.Autumn:
                T0 = 20;
                break;
            case ENVSeason.Winter:
                T0 = 0;
                break;
        }
        // 天气影响
        switch (ConfigData.weather)
        {
            case ENVweather.Sunny:
                stiffness = 1;
                break;
            case ENVweather.cloudy:
                stiffness = 1;
                break;
            case ENVweather.Rainy:
                stiffness = 0.8f;
                break;
            case ENVweather.Snowy:
                stiffness = 0.5f;
                break;
        }
        // 摩擦系数的影响
        WheelFrictionCurve ff = axleInfos[0].leftWheel.forwardFriction;
        WheelFrictionCurve sf = axleInfos[0].leftWheel.sidewaysFriction;
        ff.stiffness = stiffness;
        sf.stiffness = stiffness;
        foreach (AxleInfo axleInfo in axleInfos)
        {
            axleInfo.leftWheel.forwardFriction = ff;
            axleInfo.leftWheel.sidewaysFriction = sf;
            axleInfo.rightWheel.forwardFriction = ff;
            axleInfo.rightWheel.sidewaysFriction = sf;
        }
    }

    void InitializeOnce()
	{
		// 共享内存
		mUIPC = new UIPC();
		// 获取物理引擎对象
		mRig = axleInfos[0].leftWheel.attachedRigidbody;
		// 初始化共享内存
		if (!mUIPC.AlreadyMapped)
		{
			// 配置初始化
			UIPC.cfgData.autoGear = true;
			UIPC.cfgData.maxGearNo = 5;
			UIPC.cfgData.carWeight = 50000;
			UIPC.cfgData.inputType = InputDeviceType.PC;
			UIPC.cfgData.maxVelocity = 140;
			UIPC.cfgData.maxMotorTorque = 9000;
			UIPC.cfgData.maxBrakeTorque = 500;
			UIPC.cfgData.maxSteeringAngle = 18;

			UIPC.cfgData.p = UIPC.cfgData.q = UIPC.cfgData.r = 0;
            UIPC.cfgData.x = 0; UIPC.cfgData.y = 1.86f; UIPC.cfgData.z = 0;
            UIPC.cfgData.centerX = 0; UIPC.cfgData.centerY = 0.2f; UIPC.cfgData.centerZ = 0;
            // 环境参数
            UIPC.cfgData.simTime = 0;
            UIPC.cfgData.weather = ENVweather.Sunny;
            UIPC.cfgData.season = ENVSeason.Summary;

            // 输入初始化
            UIPC.inData.cmd = InputCommand.None;
            UIPC.inData.V1 = UIPC.inData.V2 = UIPC.inData.V3 = UIPC.inData.V4 = 0;
            UIPC.inData.fAcce = 0;
			UIPC.inData.fBrake = 0;
			UIPC.inData.fHandBrake = 0;
			UIPC.inData.fSteeriing = 0;
			UIPC.inData.gearNo = GearPosition.P;

			// 写入内存映射
			mUIPC.InitConfigData();
			mUIPC.InitInData();
		}
		// 加载配置参数
		LoadConfigData();
	}

	// 初始化输出数据
	void InitOutData()
	{
		// 输出初始化
		UIPC.outData.x = 0; UIPC.outData.y = 0; UIPC.outData.z = 0;
		UIPC.outData.p = 0; UIPC.outData.q = 0; UIPC.outData.r = 0;
		UIPC.outData.vx= 0; UIPC.outData.vy= 0; UIPC.outData.vz = 0;
		UIPC.outData.L= L0; UIPC.outData.n = 0; UIPC.outData.T = T0;
        UIPC.outData.Tgear = UIPC.outData.Toil = UIPC.outData.Poil = T0;

        // 本地初始化
        mStage = RunStage.Run;
		mLastCmd = InputCommand.None;
		nGearNo = 0; GearNo = GearPosition.P;
		throttle = motor = braker = hbraker = steering = 0;
	}

	#endregion

	#region 更新

	// 帧更新
	public void Update()
	{
        if (ConfigData.inputType != InputDeviceType.Remote)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                Startup();
            }
            else if (Input.GetKeyDown("p"))
            {
                Shutdown();
            }
        }

        // 更新输出数据
        UpdateOutData();
	}

	// 实时数据更新
	public void FixedUpdate()
	{
		// 输入控制
		UpdateInput(ConfigData.inputType);
		// 驱动模型
		foreach (AxleInfo axleInfo in axleInfos)
		{
			if (axleInfo.steering)
			{
				// 方向盘
				axleInfo.leftWheel.steerAngle  = steering;
				axleInfo.rightWheel.steerAngle = steering;
				// 手刹
				axleInfo.leftWheel.brakeTorque  = hbraker;
				axleInfo.rightWheel.brakeTorque = hbraker;
			}
			if (axleInfo.motor)
			{
				// 驱动轮
				axleInfo.leftWheel.motorTorque  = motor;
				axleInfo.rightWheel.motorTorque = motor;
				// 刹车
				axleInfo.leftWheel.brakeTorque  = braker;
				axleInfo.rightWheel.brakeTorque = braker;
			}
			// 更新车轮位置, 放在 Update() 更合适吗?
			ApplyLocalPositionToVisuals(axleInfo.leftWheel);
			ApplyLocalPositionToVisuals(axleInfo.rightWheel);
		}
		// 抓地力
		AddDownForce();
		// 限速
		ClapGearSpeed();
	}

	// 更新输入控制
	private void UpdateInput(InputDeviceType inType)
	{
		if (inType != InputDeviceType.Remote)
		{
			throttle= motor = Mathf.Clamp01(Input.GetAxis("Vertical")); // 注意取值范围 [0, 1]
			braker  =-Mathf.Clamp(Input.GetAxis("VBrake"), -1, 0);      // 注意取值范围 [-1,0] --> [0,1]
			steering= Mathf.Clamp(Input.GetAxis("Horizontal"), -1, 1);
		}
		else
		{
			mUIPC.UpdateInData();
			// 处理命令
			if (UIPC.inData.cmd != mLastCmd)
			{
				switch(UIPC.inData.cmd)
				{
				case InputCommand.Reset:
					Reset();
					break;
				case InputCommand.Pause:
					Pause();
					break;
				case InputCommand.Run:
					Continue();
					break;
				case InputCommand.Startup:
					Startup();
					break;
				case InputCommand.Shutdown:
					Shutdown();
					break;
				}
				mLastCmd = UIPC.inData.cmd;
			}
			throttle= motor = Mathf.Clamp01(UIPC.inData.fAcce);
			braker  = Mathf.Clamp01(UIPC.inData.fBrake);
			hbraker = Mathf.Clamp01(UIPC.inData.fHandBrake);
			steering= Mathf.Clamp(UIPC.inData.fSteeriing, -1, 1);
		}
		// HACK: 减速测试
		//hbraker = braker = 1;
		// HACK: 加速测试
		//motor = 1;

		motor   *= MaxMotorTorque;
		braker  *= MaxBrakeTorque;
		hbraker *= MaxBrakeTorque;
		steering*= MaxSteeringAngle;

		motor += mIdleTorque;
		// 档位处理
		SetGearPosition(inType);

		switch (GearNo)
		{
		case GearPosition.N:
			motor = 0;
			break;
		case GearPosition.P:
			motor = 0;
			braker = MaxBrakeTorque;
			break;
		case GearPosition.R:
			motor *= -0.3f;
			break;
		}
	}

	// 更新输出数据
	void UpdateOutData()
	{
#if USE_XYZ
		// xb ==> Z, yb ==> Y, zb ==> X; wxb ==> -wz(r), wyb ==> -wy(q), wzb ==> -wx(p)
		UIPC.outData.x = mRig.position.z; UIPC.outData.y = mRig.position.y; UIPC.outData.z = mRig.position.x;
		UIPC.outData.vx= mRig.velocity.z; UIPC.outData.vy= mRig.velocity.y; UIPC.outData.vz= mRig.velocity.x;
		UIPC.outData.p =-mRig.rotation.eulerAngles.z;
		UIPC.outData.q =-mRig.rotation.eulerAngles.y;
		UIPC.outData.r =-mRig.rotation.eulerAngles.x;
#else
		UIPC.outData.x = mRig.position.x; UIPC.outData.y = mRig.position.y; UIPC.outData.z = mRig.position.z;
		UIPC.outData.vx= mRig.velocity.x; UIPC.outData.vy= mRig.velocity.y; UIPC.outData.vz= mRig.velocity.z;
		UIPC.outData.p = mRig.rotation.eulerAngles.x;
		UIPC.outData.q = mRig.rotation.eulerAngles.y;
		UIPC.outData.r = mRig.rotation.eulerAngles.z;
#endif
		// 转速插值
		if (mStage == RunStage.Run)
		{
			UIPC.outData.n = Mathf.Lerp(N1, N2, throttle);
			UIPC.outData.T = Mathf.Lerp(T1, T2, throttle);
			UIPC.outData.L -= Mathf.Lerp(dL1, dL2, throttle);
			if (UIPC.outData.L < L1) mStage = RunStage.Stop;
		}
		mUIPC.UpdateOutData();
//		mUIPC.Flush();
	}

	#endregion

	#region 功能

	// 手动换挡
	private void SetGearPosition(InputDeviceType inType)
	{
		if (inType == InputDeviceType.Remote)
			GearNo = UIPC.inData.gearNo;
		else if(Input.GetButton("G1"))
			GearNo = GearPosition.S1;
		else if (Input.GetButton("G2"))
			GearNo = GearPosition.S2;
		else if (Input.GetButton("G3"))
			GearNo = GearPosition.S3;
		else if (Input.GetButton("G4"))
			GearNo = GearPosition.S4;
		else if (Input.GetButton("G5"))
			GearNo = GearPosition.S5;
		else if (Input.GetButton("GD"))
			GearNo = GearPosition.D;
		else if (Input.GetButton("GR"))
			GearNo = GearPosition.R;
		else if (Input.GetButton("GP"))
			GearNo = GearPosition.P;
		else
			GearNo = GearPosition.N;
	}

	// 自动换挡
	private void AutoChangeGear()
	{
		if (nGearNo < 1)
		{
			if(IsAutoGear)
				nGearNo = 1;
			else
				nGearNo = 0;
			return; // N 档不变速
		}
		else if(nGearNo > ConfigData.maxGearNo)
		{
			nGearNo = ConfigData.maxGearNo;
		}

		//if (GearNo != GearPosition.D) return;
		float v2 = mRig.velocity.sqrMagnitude;
		float vu = gearsNoVels[nGearNo - 1] / 3.6f;
		float vd = 0;
		if (nGearNo > 1) vd = gearsNoVels[nGearNo - 2] / 3.6f;
		vu *= vu; vd *= vd;

		if ((nGearNo < ConfigData.maxGearNo) && (v2 > vu * 0.8f))
			nGearNo++;
		else if((nGearNo > 1) && (v2 < vd * 0.8f))
			nGearNo--;
	}

	// 根据档位限制速度
	private void ClapGearSpeed()
	{
		float topSpeed, speed;
		// N 档位不限速
		if (GearNo == GearPosition.N)
			return;

		try
		{
			topSpeed = gearsNoVels[nGearNo-1] / 3.6f;
		}
		catch
		{
			topSpeed = 33; // 120 km/h
		}
		if (topSpeed > MaxVelocity)
			topSpeed = MaxVelocity;
		speed = mRig.velocity.magnitude;

		if (speed > topSpeed)
			mRig.velocity = topSpeed * mRig.velocity.normalized;
	}

	// 增加抓地力
	private void AddDownForce()
	{
		mRig.AddForce(-transform.up * mDownforce * mRig.velocity.sqrMagnitude);
	}

	// 查找轮子视觉模型, 并进行位置同步
	public void ApplyLocalPositionToVisuals(WheelCollider collider)
	{
		if (collider.transform.childCount == 0)
		{
			return;
		}

		Transform visualWheel = collider.transform.GetChild(0);

		Vector3 position;
		Quaternion rotation;
		collider.GetWorldPose(out position, out rotation);

		visualWheel.transform.position = position;
		visualWheel.transform.rotation = rotation;
	}

	#endregion

	#region IMGUI

	void OnGUI () {
		float x = Screen.width - 120; // 右
//		float y = Screen.height - 50; // 下
		float x1 = x + 10;
		float x2 = x1+ 50;
		// Make a background box
		GUI.Box(new Rect (x,0,120,100), "Car Information");
		// Velocity
		GUI.Label (new Rect (x1,25,50,50), "VEL:");
		GUI.Label (new Rect (x2,25,100,50), string.Format("{0:F1}", mRig.velocity.magnitude * 3.6));
		// RPM
		GUI.Label (new Rect (x1,50,50,50), "RPM:");
		GUI.Label (new Rect (x2,50,100,50), string.Format("{0:F1}", UIPC.outData.n));
		// Z-Distance
		GUI.Label (new Rect (x1,75,50,50), "Disz:");
		GUI.Label (new Rect (x2,75,100,50), string.Format("{0:F1}", UIPC.outData.z));
	}

	#endregion

	#region 命令事件

	// 启动
	void Startup()
	{
		if(mStage == RunStage.Stop)
		{
			StartCoroutine(Starting());
		}
	}

	// 停车
	void Shutdown()
	{
		if (mStage != RunStage.Stop)
		{
			StartCoroutine(Stopping());
		}
	}

	// 重置
	void Reset()
	{
		// 配置初始化
		LoadConfigData();
		// 输出初始化
		InitOutData();
		// 动力学初始化
		if(mRig.isKinematic)
			mRig.isKinematic = false;
	}

	// 暂停
	void Pause()
	{
		mRig.isKinematic = true;
	}

	// 继续
	void Continue()
	{
		mRig.isKinematic = false;
	}

	#endregion

	#region 协同例程

	IEnumerator Starting()
	{
		for (int f = 0; mStage == RunStage.Stop; f++)
		{
			UIPC.outData.n = Mathf.Lerp(0, 1200, f * Time.deltaTime /10);

			if (UIPC.outData.n > 1100) mStage = RunStage.Run;

			yield return null;
		}
	}

	IEnumerator Idling()
	{
		while (mStage == RunStage.Init)
		{
			UIPC.outData.n -= 1;

			if (UIPC.outData.n < 960) mStage = RunStage.Run;
			yield return new WaitForSeconds(0.1f);
		}
	}

	IEnumerator Stopping()
	{
		if (mStage == RunStage.Stop)
			yield return null;
		else
		{
			mStage = RunStage.Down;
			while (mStage == RunStage.Down)
			{
				UIPC.outData.n -= 10;

				if (UIPC.outData.n < 10)
				{
					UIPC.outData.n = 0;
					mStage = RunStage.Stop;
				}
				yield return new WaitForSeconds(0.1f);
			}
		}
	}

	IEnumerator Consuming()
	{
		if (mStage != RunStage.Run)
			yield return null;
		else
		{
			UIPC.outData.L -= Mathf.Lerp(0.5f, 1.2f, throttle);

			yield return new WaitForSeconds(0.1f);
		}
	}
	#endregion
}