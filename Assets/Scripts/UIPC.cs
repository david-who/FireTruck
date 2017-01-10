using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;

#region 数据定义

public enum InputDeviceType
{
	PC       = 0, // Native Keyboard & Mouse
	JoyStick = 1, // JoyStick not recognized
	Remote   = 2, // Network
}

public enum GearPosition
{
	N  = 0, // 空挡
	S1 = 1, // 1 挡
	S2 = 2, // 2 挡
	S3 = 3, // 3 挡
	S4 = 4, // 4 挡
	S5 = 5, // 5 挡
	S6 = 6, // 6 挡
	S7 = 7, // 7 挡
	S8 = 8, // 8 挡
	S9 = 9, // 9 挡
	S10=10, // 10挡
	S11=11, // 11挡
	D = 12, // 自动挡
	R = 13, // 倒挡
	S = 14, // 手动挡
	P = 15, // 驻车挡
}

public enum InputCommand
{
	None = 0,  // 无指令, 忽略
	Reset= 1,  // 重置, 初始化
	Run  = 2,  // 运行
	Pause= 3,  // 暂停
	Load = 4,  // 加载参数
	Startup =5,// 启动
	Shutdown=6,// 熄火
	Exit = 15  // 退出程序
}

public enum ENVSeason
{
    Spring = 0,   // 春
    Summary= 32,  // 夏
    Autumn = 64,  // 秋
    Winter = 128, // 冬
}

public enum ENVweather
{
    Sunny = 0,   // 晴
    cloudy= 32,  // 阴
    Rainy = 64,  // 雨
    Snowy = 128, // 雪
}

[System.Serializable]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct ConfigInfo
{
    #region  配置参数 
    public int maxGearNo;             // 最大档位
	public bool autoGear;             // 自动换挡
	public InputDeviceType inputType; // 输入方式
	public float carWeight;           // 车体重量 (kg)
	public float maxVelocity;         // 最大车速 (km/h)
	public float maxMotorTorque;      // 最大驱动扭矩 (N.M)
	public float maxBrakeTorque;      // 最大刹车扭矩 (N.M)
	public float maxSteeringAngle;    // 最大转向偏转 (degree)
    public float centerX, centerY, centerZ; // 质心偏移 [0, 0.2, 0]
    #endregion

    #region 初始状态 
    public float x, y, z;             // 位置坐标 (m)
	public float p, q, r;             // 绕 X, Y, Z 轴旋转角度 (deg)
    #endregion

    #region  环境参数 
    public long simTime;              // 仿真时间 (s)
    public ENVSeason season;          // 仿真季节
    public ENVweather weather;        // 仿真天气
    #endregion
}

[System.Serializable]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct InputData
{
    #region   控制命令 
    public InputCommand cmd;     // 功能命令
    public float V1, V2, V3, V4; // 命令参数
    #endregion

    #region 操纵数据 
    public GearPosition gearNo;  // 档位
	public float fAcce;          // 油门 [0, 1]
	public float fBrake;         // 刹车 [0, 1]
	public float fHandBrake;     // 手刹 [0, 1]
	public float fSteeriing;     // 转向 [-1,1]
    #endregion
}

[System.Serializable]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct OutData
{
    #region 运动信息
    public float x, y, z;        // 欧拉坐标
	public float p, q, r;        // 欧拉角度
	public float vx, vy, vz;     // 速度矢量
    #endregion

    #region 仪表信息
    public float n;              // 转速、油量、温度
    public float T;              // 冷却液温度
    public float L;              // 剩余燃油 （L）
    public float Tgear;          // 变速箱温度
    public float Toil;           // 机油温度
    public float Poil;           // 机油压力（bar）
    #endregion
}

#endregion

public class UIPC {

	#region 常数定义

	const int AddrCfg = 0;   // [0-511]
	const int AddrIn  = 512; // [512-767]
	const int AddrOut = 768; // [768-1023]

#if UNITY_STANDALONE_WIN
	const string sharedFile = @"PumperShared.dat";
	const string sharedName = @"Local\PumperFileMappingObject";
#else
	const int sharedID = 93199;
#endif

	readonly int nCfgLen, nInLen, nOutLen;
	private MappedMemory mm; byte[] buffer;

	#endregion

	// Use this for initialization
	public UIPC() {
		buffer  = new byte[1024];
		nCfgLen = Marshal.SizeOf(cfgData);
		nInLen  = Marshal.SizeOf(inData);
		nOutLen = Marshal.SizeOf(outData);

#if UNITY_STANDALONE_WIN
		mm = new MappedMemory(sharedName, 1024, sharedFile);
#else
		mm = new MappedMemory(sharedID, 1024);
#endif
	}

	~UIPC()
	{
		Dispose();
	}

	public bool AlreadyMapped
	{
		get { return (mm==null)?false:mm.Already; }
	}

	public void Flush()
	{
		mm.Flush();
	}

	public void Dispose()
	{
		buffer = null;

		if (mm != null)
		{
			mm.Flush();
			mm.Dispose();
			mm = null;
		}
	}

	/// <summary> 初始化配置数据 </summary>
	public void InitConfigData()
	{
		lock (buffer)
		{
			IntPtr ptr = Marshal.AllocCoTaskMem(nCfgLen);
			try
			{
				Marshal.StructureToPtr(cfgData, ptr, false);
				Marshal.Copy(ptr, buffer, 0, nCfgLen);
			}
			finally
			{
				Marshal.FreeCoTaskMem(ptr);
			}

			mm.Position = AddrCfg;
			mm.WriteA(buffer, nCfgLen);
		}
	}

	/// <summary> 初始化配置数据 </summary>
	public void InitInData()
	{
		lock (buffer)
		{
			IntPtr ptr = Marshal.AllocCoTaskMem(nInLen);
			try
			{
				Marshal.StructureToPtr(inData, ptr, false);
				Marshal.Copy(ptr, buffer, 0, nInLen);
			}
			finally
			{
				Marshal.FreeCoTaskMem(ptr);
			}

			mm.Position = AddrIn;
			mm.WriteA(buffer, nInLen);
		}
	}

	/// <summary> 更新输出数据 </summary>
	public void UpdateOutData()
	{
		lock (buffer)
		{
			IntPtr ptr = Marshal.AllocHGlobal(nOutLen);
			try
			{
				Marshal.StructureToPtr(outData, ptr, false);
				Marshal.Copy(ptr, buffer, 0, nOutLen);
			}
			finally
			{
				// Free the unmanaged memory.
				Marshal.FreeHGlobal(ptr);
			}

			mm.Position = AddrOut;
			mm.WriteA(buffer, nOutLen);
		}
	}

	/// <summary> 更新输入数据 </summary>
	public void UpdateInData()
	{
		lock (buffer)
		{
			mm.Position = AddrIn;
			mm.ReadA(buffer, nInLen);

			IntPtr ptr = Marshal.AllocHGlobal(nInLen);
			try
			{
				Marshal.Copy(buffer, 0, ptr, nInLen);
				inData = (InputData)Marshal.PtrToStructure(ptr, typeof(InputData));
			}
			finally
			{
				// Free the unmanaged memory.
				Marshal.FreeHGlobal(ptr);
			}
		}
	}

	/// <summary> 更新配置数据 </summary>
	public void UpdateConfigData()
	{
		lock (buffer)
		{
			mm.Position = AddrCfg;
			mm.ReadA(buffer, nCfgLen);

			IntPtr ptr = Marshal.AllocCoTaskMem(nCfgLen);
			try
			{
				Marshal.Copy(buffer, 0, ptr, nCfgLen);
				cfgData = (ConfigInfo)Marshal.PtrToStructure(ptr, typeof(ConfigInfo));
			}
			finally
			{
				// Free the com memory.
				Marshal.FreeCoTaskMem(ptr);
			}
		}
	}

#if DEBUG
	public void ReadOutData()
	{
		lock (buffer)
		{
			mm.Position = AddrOut;
			mm.ReadA(buffer, nOutLen);

			IntPtr ptr = Marshal.AllocHGlobal(nOutLen);
			try
			{
				Marshal.Copy(buffer, 0, ptr, nOutLen);
				outData = (OutData)Marshal.PtrToStructure(ptr, typeof(OutData));
			}
			finally
			{
				// Free the unmanaged memory.
				Marshal.FreeHGlobal(ptr);
			}
		}
	}
#endif

	#region 共享数据

	internal static OutData    outData;
	internal static InputData  inData;
	internal static ConfigInfo cfgData;

	#endregion
}


#if UNITY_STANDALONE_WIN

[Flags]
public enum MapAccess
{
	FileMapCopy = 0x0001,
	FileMapWrite = 0x0002,
	FileMapRead = 0x0004,
	FileMapExecute = 0x0020,
	FileMapAllAccess = 0x0f001f,
}

[Flags]
public enum MapProtection
{
	PageNone = 0x00000000,
	// protection
	PageNoAccess = 0x01,
	PageReadOnly = 0x02,
	PageReadWrite = 0x04,
	PageWriteCopy = 0x08,
	// attributes
	SecFile = 0x00800000,
	SecImage = 0x01000000,
	SecReserve = 0x04000000,
	SecCommit = 0x08000000,
	SecNoCache = 0x10000000,
}

/// <summary>
/// IPC 函数封装
/// </summary>
internal static class NIPC
{
	[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
	public static extern IntPtr CreateFile(
		String lpFileName,          // LPCTSTR lpFileName,                          // file name 
		int dwDesiredAccess,        // DWORD dwDesiredAccess,                       // access mode 
		int dwShareMode,            // DWORD dwShareMode,                           // share mode
		IntPtr lpAttributes,        // LPSECURITY_ATTRIBUTES lpSecurityAttributes,  // SD 
		int dwCreationDisposition,  // DWORD dwCreationDisposition,                 // how to create
		int dwFlagsAndAttributes,   // DWORD dwFlagsAndAttributes,                  // file attributes
		IntPtr hTemplateFile);      // HANDLE hTemplateFile                         // handle to template file 


	[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
	public static extern IntPtr CreateFileMapping(
		IntPtr hFile,             // 0xFFFFFFFF(-1) 使用系统分页文件, Win32 (IntPtr=int32)使用 uint(64) 类型
		IntPtr lpAttributes,      // LPSECURITY_ATTRIBUTES lpSecurityAttributes
		int flProtect,            // MapProtection
		int dwMaximumSizeHigh,    // high-order DWORD of the maximum size 
		int dwMaximumSizeLow,     // low-order DWORD of the maximum size 
		String lpName);

	[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
	public static extern IntPtr OpenFileMapping(
		int dwDesiredAccess,
		bool bInheritHandle,
		String lpName);

	[DllImport("kernel32", SetLastError = true)]
	public static extern bool CloseHandle(IntPtr handle);

	[DllImport("kernel32", SetLastError = true)]
	public static extern IntPtr MapViewOfFile(
		IntPtr hFileMappingObject,
		int dwDesiredAccess,
		int dwFileOffsetHigh,
		int dwFileOffsetLow,
		int dwNumBytesToMap);

	[DllImport("kernel32", SetLastError = true)]
	public static extern bool FlushViewOfFile(
		IntPtr lpBaseAddress,
		int dwNumBytesToFlush);

	[DllImport("kernel32", SetLastError = true)]
	public static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);
}

/// <summary> (文件)映射内存 </summary>
public class MappedMemory : Stream
{
	#region 常量

	public const short FILE_ATTRIBUTE_NORMAL = 0x80;
	public const int GENERIC_READ = unchecked((int)0x80000000);
	public const int GENERIC_WRITE = 0x40000000;
	public const int FILE_SHARE_READ = 0x00000001;
	public const int FILE_SHARE_WRITE = 0x00000002;
	public const int SHARE_READ_WRITE = 0x00000003;
	public const int FILE_SHARE_DELETE = 0x00000004;
	public const int CREATE_NEW = 1;
	public const int CREATE_ALWAYS = 2;
	public const int OPEN_EXISTING = 3;
	public const int OPEN_ALWAYS = 4;
	public const int TRUNCATE_EXISTING = 5;

	readonly IntPtr NULL_HANDLE = new IntPtr(0);
	readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

	[StructLayout(LayoutKind.Sequential)]
	protected class OFSTRUCT
	{
		public const int OFS_MAXPATHNAME = 128;
		public byte cBytes;        // BYTE cBytes;
		public byte fFixedDisc;    // BYTE fFixedDisk; 
		public UInt16 nErrCode;    // WORD nErrCode;
		public UInt16 Reserved1;   // WORD Reserved1;
		public UInt16 Reserved2;   // WORD Reserved2;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = OFS_MAXPATHNAME)]
		public string szPathName;  // CHAR szPathName[OFS_MAXPATHNAME];
	}

	[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
	protected static extern IntPtr OpenFile(
		String lpFileName,     // file name 
		[Out, MarshalAs(UnmanagedType.LPStruct)]
		OFSTRUCT lpReOpenBuff, // file information 
		int uStyle);           // action and attributes 

	#endregion

	#region 变量

	IntPtr m_hMap, m_hFile;
	IntPtr m_baseAddr, m_currentAddr;
	long m_maxSize; bool m_isWritable;

	#endregion

	/// <summary> 初始化对象, 需要调用 Open/OpenEX </summary>
	public MappedMemory()
	{
		m_hFile = m_hMap = IntPtr.Zero;
		m_currentAddr = m_baseAddr = IntPtr.Zero;
		m_maxSize = 0; m_isWritable = true; Already = false;
	}

	/// <summary> 初始化共享内存对象, 不需要调用 Open/OpenEX </summary>
	public MappedMemory(String shareName, long maxSize, long offset = 0,
		MapProtection protection = MapProtection.PageReadWrite,
		MapAccess access = MapAccess.FileMapAllAccess)
	{
		m_maxSize = 0; Already = false;
		m_currentAddr = m_baseAddr = IntPtr.Zero;
		m_isWritable = ((access & MapAccess.FileMapWrite) == MapAccess.FileMapWrite) ? true : false;
		// 系统分页: 共享内存 0xFFFFFFFF
		m_hFile = INVALID_HANDLE_VALUE;
		try
		{   // 1.1) 试图打开已存在的共享文件
			m_hMap = NIPC.OpenFileMapping((int)access, false, shareName);
			if (m_hMap == NULL_HANDLE)
			{
				int desiredAccess = GENERIC_READ;
				if ((protection & MapProtection.PageReadWrite) == MapProtection.PageReadWrite)
				{
					desiredAccess |= GENERIC_WRITE;
					m_isWritable = true;
				}
				else
					m_isWritable = false;
				// 1.2) 创建共享文件
				m_hMap = NIPC.CreateFileMapping(m_hFile, IntPtr.Zero, (int)protection,
					(int)(maxSize >> 32), (int)(maxSize & 0xFFFFFFFF), shareName);
				if (m_hMap == NULL_HANDLE)
					throw new Exception(Marshal.GetHRForLastWin32Error().ToString());
			}
			else
				Already = true;
			m_maxSize = maxSize;
			ShareName = shareName;
			// 2) 映射内存
			m_currentAddr = m_baseAddr = NIPC.MapViewOfFile(m_hMap, (int)access,
				(int)((offset >> 32) & 0xFFFFFFFF), (int)(offset & 0xFFFFFFFF), (int)m_maxSize);
		}
		catch (Exception Err)
		{
			Close();
			throw Err;
		}
	}

	/// <summary> 初始化对象, 不需要调用 Open/OpenEX.
	/// 首先试图打开共享内存, 否则创建文件映射 </summary>
	public MappedMemory(String shareName, long maxSize, String fileName, long offset = 0,
		MapProtection protection = MapProtection.PageReadWrite,
		MapAccess access = MapAccess.FileMapAllAccess)
	{
		m_maxSize = 0; Already = false;
		m_currentAddr = m_baseAddr = IntPtr.Zero;
		m_isWritable = ((access & MapAccess.FileMapWrite) == MapAccess.FileMapWrite) ? true : false;
		m_hFile = INVALID_HANDLE_VALUE;
		try
		{   // 1.1) 试图打开已存在的共享文件
			m_hMap = NIPC.OpenFileMapping((int)access, false, shareName);
			if (m_hMap == NULL_HANDLE)
			{
				int desiredAccess = GENERIC_READ;
				if ((protection & MapProtection.PageReadWrite) == MapProtection.PageReadWrite)
				{
					desiredAccess |= GENERIC_WRITE;
					m_isWritable = true;
				}
				else
					m_isWritable = false;
				// 1.2) 创建共享文件
				m_hFile = NIPC.CreateFile(fileName, desiredAccess, SHARE_READ_WRITE, IntPtr.Zero, OPEN_ALWAYS, 0, IntPtr.Zero);
				if (m_hFile != NULL_HANDLE)
				{
					m_hMap = NIPC.CreateFileMapping(m_hFile, IntPtr.Zero, (int)protection, 0, (int)maxSize, shareName);
					if (m_hMap == NULL_HANDLE)
						throw new Exception(Marshal.GetHRForLastWin32Error().ToString());
				}
				else
				{
					throw new Exception(Marshal.GetHRForLastWin32Error().ToString());
				}
			}
			else
				Already = true;
			m_maxSize = maxSize;
			ShareName = shareName;
			// 2) 映射内存
			m_currentAddr = m_baseAddr = NIPC.MapViewOfFile(m_hMap, (int)access,
				(int)((offset >> 32) & 0xFFFFFFFF), (int)(offset & 0xFFFFFFFF), (int)m_maxSize);
		}
		catch (Exception Err)
		{
			Close();
			throw Err;
		}
	}

	/// <summary> 试图打开共享内存 </summary>
	public bool Open(String shareName, int count = 512, MapAccess access = MapAccess.FileMapAllAccess)
	{
		bool RV = false;
		m_isWritable = ((access & MapAccess.FileMapWrite) == MapAccess.FileMapWrite) ? true : false;
		try
		{
			// 1) 打开已存在的共享文件
			m_hMap = NIPC.OpenFileMapping((int)access, false, shareName);
			if (m_hMap != NULL_HANDLE)
			{
				// 2) 映射内存
				m_currentAddr = m_baseAddr = NIPC.MapViewOfFile(m_hMap, (int)access,
					0, 0, 0); // 0: 使用文件长度
				RV = true;
				m_maxSize = count;
				ShareName = shareName;
				Already = true;
			}
			return RV;
		}
		catch
		{
			return RV;
		}
	}

	/// <summary> 首先试图打开共享内存, 否则打开文件映射, 如果文件不在也不创建. </summary>
	public bool OpenEx(String shareName, long maxSize, String fileName, long offset = 0,
		MapProtection protection = MapProtection.PageReadWrite,
		MapAccess access = MapAccess.FileMapAllAccess)
	{
		bool RV = true;
		m_hFile = INVALID_HANDLE_VALUE;
		m_isWritable = ((access & MapAccess.FileMapWrite) == MapAccess.FileMapWrite) ? true : false;
		try
		{   // 1.1) 试图打开已存在的共享文件
			m_hMap = NIPC.OpenFileMapping((int)access, true, shareName);
			if (m_hMap == NULL_HANDLE)
			{
				// determine file access needed
				// we'll always need generic read access
				int desiredAccess = GENERIC_READ;
				if ((protection == MapProtection.PageReadWrite)
					|| (protection == MapProtection.PageWriteCopy))
				{
					desiredAccess |= GENERIC_WRITE;
				}

				if (System.IO.File.Exists(fileName))
				{
					OFSTRUCT ipStruct = new OFSTRUCT();
					m_hFile = OpenFile(fileName, ipStruct, 2);
				}
				// 1.2) 创建共享文件 
				m_hMap = NIPC.CreateFileMapping(m_hFile, IntPtr.Zero, (int)protection, 0, (int)maxSize, shareName);
				if (m_hMap == NULL_HANDLE)
				{
					RV = false;
					m_maxSize = 0;
				}
			}
			else
				Already = true;
			// 2) 映射内存
			m_maxSize = maxSize;
			ShareName = shareName;
			m_currentAddr = m_baseAddr = NIPC.MapViewOfFile(m_hMap, (int)access,
				(int)((offset >> 32) & 0xFFFFFFFF), (int)(offset & 0xFFFFFFFF), (int)m_maxSize);
			return RV;
		}
		catch
		{
			return false;
		}
	}

	/// <summary> 直接读取流, 不更新流位置 </summary>
	/// <param name="count"> 0: 使用 buffer 长度</param>
	/// <returns> 返回读取的字节数 </returns>
	public int ReadA(byte[] buffer, int count = 0)
	{
		if (count == 0) count = buffer.Length;

		long nMax = count + Position;
		if (nMax > m_maxSize)
		{
			count = (int)(m_maxSize - Position);
		}
		if (count > 0)
		{
			Marshal.Copy(m_currentAddr, buffer, 0, count);
		}
		else
			count = 0;
		return count;
	}

	/// <summary> 直接写入流, 不更新流位置 </summary>
	/// <param name="count"> 0: 使用 buffer 长度</param>
	/// <returns> 返回写入的字节数 </returns>
	public int WriteA(byte[] buffer, int count = 0)
	{
		if (!CanWrite) return -1;
		if (count == 0) count = buffer.Length;

		long nMax = count + Position;
		if (nMax > m_maxSize)
		{
			count = (int)(m_maxSize - Position);
		}
		if (count > 0)
		{
			Marshal.Copy(buffer, 0, m_currentAddr, count);
		}

		return count;
	}

	public void Write(string str)
	{
		try
		{
			Marshal.Copy(str.ToCharArray(), 0, m_currentAddr, str.Length);
		}
		catch (Exception Err)
		{
			throw Err;
		}
	}

	public string Read()
	{
		try
		{
			return Marshal.PtrToStringUni(m_currentAddr);
		}
		catch (Exception Err)
		{
			throw Err;
		}
	}

	public override void Close()
	{
		if (m_baseAddr != NULL_HANDLE)
			NIPC.UnmapViewOfFile(m_baseAddr);

		if (m_hMap != NULL_HANDLE)
			NIPC.CloseHandle(m_hMap);

		if ((m_hFile != NULL_HANDLE) && (m_hFile != INVALID_HANDLE_VALUE))
			NIPC.CloseHandle(m_hFile);

		m_hFile = m_hMap = IntPtr.Zero;
		m_currentAddr = m_baseAddr = IntPtr.Zero;
	}

	#region 属性

	public bool Already
	{
		get;
		private set;
	}

	public IntPtr HeapAddress
	{
		get { return m_baseAddr; }
		internal protected set { m_baseAddr = value; }
	}

	public string ShareName
	{
		get;
		set;
	}

	#endregion

	#region IStream

	#region 属性

	/// <summary> 堆栈长度 </summary>
	public override long Length
	{
		get { return m_maxSize; }
	}

	/// <summary> 可读 </summary>
	public override bool CanRead
	{
		get { return true; }
	}

	/// <summary> 随机存储 </summary>
	public override bool CanSeek
	{
		get { return true; }
	}

	/// <summary> 可写 </summary>
	public override bool CanWrite
	{
		get { return m_isWritable; }
	}

	/// <summary> 存储偏移 </summary>
	public override long Position
	{
		get
		{
			return (m_currentAddr.ToInt64() - m_baseAddr.ToInt64());
		}
		set
		{
			long nOff = m_maxSize - 1;
			if (nOff > 0)
			{
				long offset = m_baseAddr.ToInt64();
				if (value > nOff)
					offset += nOff;
				else
					offset += value;

				m_currentAddr = new IntPtr(offset);
			}
		}
	}

	#endregion

	#region 方法

	public override void Flush()
	{
		if (m_baseAddr != NULL_HANDLE)
			NIPC.FlushViewOfFile(m_baseAddr, (int)m_maxSize);
	}

	/// <summary> 固定内存, 调用无效 </summary>
	public override void SetLength(long value) { }

	/// <summary>设置流当前位置 </summary>
	public override long Seek(long offset, SeekOrigin origin)
	{
		if (!CanSeek) return -1;

		long nOff = m_maxSize - 1;
		switch (origin)
		{
		case SeekOrigin.Begin:
			Position = offset;
			break;
		case SeekOrigin.Current:
			Position = Position + offset;
			break;
		case SeekOrigin.End:
			Position = nOff - offset;
			break;
		}
		return Position;
	}

	/// <summary> 顺序读取流, 需要 Posiotion 或 Seek 定位 </summary>
	public override int Read(byte[] buffer, int offset, int count)
	{
		if (!CanRead) return -1;

		long nMax = offset + count + Position;
		if (nMax > m_maxSize)
		{
			count = (int)(m_maxSize - offset - Position);
		}
		if (count > 0)
		{
			Marshal.Copy(m_currentAddr, buffer, offset, count);
			Position += count;
		}
		else
			count = 0;
		return count;
	}

	/// <summary> 顺序写入流, 需要 Posiotion 或 Seek 定位 </summary>
	public override void Write(byte[] buffer, int offset, int count)
	{
		if (!CanWrite) return;

		long nMax = offset + count + Position;
		if (nMax > m_maxSize)
		{
			count = (int)(m_maxSize - offset - Position);
		}
		if (count > 0)
		{
			Marshal.Copy(buffer, offset, m_currentAddr, count);
			Position += count;
		}
	}

	#endregion

	#endregion

	#region IDisposable
	// Flag: Has Dispose already been called?
	bool disposed = false;

	// Public implementation of Dispose pattern callable by consumers.
	new public void Dispose()
	{
		base.Dispose();

		Dispose(true);
		GC.SuppressFinalize(this);
	}

	// Protected implementation of Dispose pattern.
	protected override void Dispose(bool disposing)
	{
		if (disposed)
			return;

		if (disposing)
		{
			// Free any other managed objects here.
			Close();
		}

		// Free any unmanaged objects here.
		m_hMap = IntPtr.Zero;
		disposed = true;
	}

	#endregion
}

// Unix: ipcs, ipcrm 管理 信号量、共享内存、文件映射
#else // UNITY_STANDALONE_OSX

/// <summary> IPC 函数封装 </summary>
internal static class NIPC
{
	// shared memory exists
	[DllImport ("UIPC")]
	public static extern bool IsShmOpened(int key);

	// get shm ID
	[DllImport ("UIPC")]
	public static extern int OpenSharedMemory(int key, int size);

	// get shm address
	[DllImport ("UIPC")]
	public static extern IntPtr GetSharedMemory(int id, int offset);

	// close shm
	[DllImport ("UIPC")]
	public static extern int CloseSharedMemory(IntPtr ptr);

	// delete shm
	[DllImport ("UIPC")]
	public static extern int DeleteSharedMemory(int id);
}

/// <summary> (文件)映射内存 </summary>
public class MappedMemory : Stream
{
	#region 变量

	private int id;
	IntPtr m_baseAddr, m_currentAddr;
	long m_maxSize;

	#endregion

	/// <summary> 初始化对象, 需要调用 Open/OpenEX </summary>
	public MappedMemory()
	{
		id = 0; m_maxSize = 0; Already = false;
		m_currentAddr = m_baseAddr = IntPtr.Zero;
	}

	/// <summary> 初始化共享内存对象, 不需要调用 Open/OpenEX </summary>
	public MappedMemory(int key, int maxSize, int offset = 0)
	{
		m_maxSize = 0; Already = NIPC.IsShmOpened(key);
		m_currentAddr = m_baseAddr = IntPtr.Zero;
		// 1) 试图打开已存在的共享文件
		id = NIPC.OpenSharedMemory(key, maxSize);
		if (id < 0)
			throw(new Exception ("Can not create shared memory."));
		
		// 2) 映射内存
		m_maxSize = maxSize;
		m_currentAddr = m_baseAddr = NIPC.GetSharedMemory(id, offset);
		if (m_baseAddr == IntPtr.Zero)
			throw(new Exception ("Can not map shared memory."));
	}


	/// <summary> 试图打开共享内存 </summary>
	public bool Open(int key, int maxSize, int offset = 0)
	{
		// 1) 打开已存在的共享文件
		Already = NIPC.IsShmOpened(key);
		id = NIPC.OpenSharedMemory(key, maxSize);
		if (id < 0)
			return false;

		// 2) 映射内存
		m_maxSize = maxSize;
		m_currentAddr = m_baseAddr = NIPC.GetSharedMemory(id, offset);
		if (m_baseAddr == IntPtr.Zero)
			return false;
		
		return true;
	}
		
	/// <summary> 直接读取流 </summary>
	/// <param name="count"> 0: 使用 buffer 长度</param>
	/// <returns> 返回读取的字节数 </returns>
	public int ReadA(byte[] buffer, int count = 0)
	{
		if (count == 0) count = buffer.Length;

		long nMax = count + Position;
		if (nMax > m_maxSize)
		{
			count = (int)(m_maxSize - Position);
		}
		if (count > 0)
		{
			Marshal.Copy(m_currentAddr, buffer, 0, count);
		}
		else
			count = 0;
		return count;
	}

	//// <summary> 直接写入流, 不更新流位置 </summary>
	/// <param name="count"> 0: 使用 buffer 长度</param>
	/// <returns> 返回写入的字节数 </returns>
	public int WriteA(byte[] buffer, int count = 0)
	{
		if (!CanWrite) return -1;
		if (count == 0) count = buffer.Length;

		long nMax = count + Position;
		if (nMax > m_maxSize)
		{
			count = (int)(m_maxSize - Position);
		}
		if (count > 0)
		{
			Marshal.Copy(buffer, 0, m_currentAddr, count);
		}

		return count;
	}

	public void Write(string str)
	{
		try
		{
			Marshal.Copy(str.ToCharArray(), 0, m_currentAddr, str.Length);
		}
		catch (Exception Err)
		{
			throw Err;
		}
	}

	public string Read()
	{
		try
		{
			return Marshal.PtrToStringUni(m_currentAddr);
		}
		catch (Exception Err)
		{
			throw Err;
		}
	}

	public int Delete()
	{
		return NIPC.DeleteSharedMemory (id);
	}

	public override void Close()
	{
		if (m_baseAddr != IntPtr.Zero)
			NIPC.CloseSharedMemory(m_baseAddr);

		m_currentAddr = m_baseAddr = IntPtr.Zero;
	}

	#region 属性

	public IntPtr HeapAddress
	{
		get { return m_baseAddr; }
		internal protected set { m_baseAddr = value; }
	}

	public string ShareName
	{
		get;
		set;
	}

	public bool Already
	{
		get;
		private set;
	}

	#endregion

	#region IStream

	#region 属性

	/// <summary> 堆栈长度 </summary>
	public override long Length
	{
		get { return m_maxSize; }
	}

	/// <summary> 可读 </summary>
	public override bool CanRead
	{
		get { return true; }
	}

	/// <summary> 随机存储 </summary>
	public override bool CanSeek
	{
		get { return true; }
	}

	/// <summary> 可写 </summary>
	public override bool CanWrite
	{
		get { return true; }
	}

	/// <summary> 存储偏移 </summary>
	public override long Position
	{
		get
		{
			return (m_currentAddr.ToInt64() - m_baseAddr.ToInt64());
		}
		set
		{
			long nOff = m_maxSize - 1;
			if (nOff > 0)
			{
				long offset = m_baseAddr.ToInt64();
				if (value > nOff)
					offset += nOff;
				else
					offset += value;

				m_currentAddr = new IntPtr(offset);
			}
		}
	}

	#endregion

	#region 方法

	public override void Flush() { }

	/// <summary> 固定内存, 调用无效 </summary>
	public override void SetLength(long value) { }

	/// <summary>设置流当前位置 </summary>
	public override long Seek(long offset, SeekOrigin origin = SeekOrigin.Begin)
	{
		if (!CanSeek) return -1;

		long nOff = m_maxSize - 1;
		switch (origin)
		{
		case SeekOrigin.Begin:
			Position = offset;
			break;
		case SeekOrigin.Current:
			Position = Position + offset;
			break;
		case SeekOrigin.End:
			Position = nOff - offset;
			break;
		}
		return Position;
	}

	/// <summary> 顺序读取流, 需要 Posiotion 或 Seek 定位 </summary>
	public override int Read(byte[] buffer, int offset, int count)
	{
		if (!CanRead) return -1;

		long nMax = offset + count + Position;
		if (nMax > m_maxSize)
		{
			count = (int)(m_maxSize - offset - Position);
		}
		if (count > 0)
		{
			Marshal.Copy(m_currentAddr, buffer, offset, count);
			Position += count;
		}
		else
			count = 0;
		return count;
	}

	/// <summary> 顺序写入流, 需要 Posiotion 或 Seek 定位 </summary>
	public override void Write(byte[] buffer, int offset, int count)
	{
		if (!CanWrite) return;

		long nMax = offset + count + Position;
		if (nMax > m_maxSize)
		{
			count = (int)(m_maxSize - offset - Position);
		}
		if (count > 0)
		{
			Marshal.Copy(buffer, offset, m_currentAddr, count);
			Position += count;
		}
	}

	#endregion

	#endregion

	#region IDisposable
	// Flag: Has Dispose already been called?
	bool disposed = false;

	// Public implementation of Dispose pattern callable by consumers.
	new public void Dispose()
	{
		base.Dispose();

		Dispose(true);
		GC.SuppressFinalize(this);
	}

	// Protected implementation of Dispose pattern.
	protected override void Dispose(bool disposing)
	{
		if (disposed)
			return;

		if (disposing)
		{
			// Free any other managed objects here.
			Close();
		}

		// Free any unmanaged objects here.
		disposed = true;
	}

	#endregion
}

#endif

