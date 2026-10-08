using System.Buffers.Binary;
using System.Text;

namespace DodecaDev.GpuZ.GpuZ;

/// <summary>
/// Parses the <c>GPUZShMem</c> block GPU-Z publishes. The layout is GPU-Z's packed (<c>#pragma pack(1)</c>)
/// <c>GPUZ_SH_MEM</c> struct:
/// <code>
/// UINT32 version; LONG busy; UINT32 lastUpdate;
/// GPUZ_RECORD        data[128];     // WCHAR key[256]; WCHAR value[256];
/// GPUZ_SENSOR_RECORD sensors[128];  // WCHAR name[256]; WCHAR unit[8]; UINT32 digits; double value;
/// </code>
/// </summary>
public static class GpuZSharedMemoryLayout
{
	public const string MapName = "GPUZShMem";

	public const int MaxRecords = 128;
	public const int MaxSensors = 128;

	private const int HeaderSize = 12;
	private const int BusyOffset = 4;

	private const int RecordKeyBytes = 256 * sizeof(char);
	private const int RecordValueBytes = 256 * sizeof(char);
	private const int RecordSize = RecordKeyBytes + RecordValueBytes;

	private const int SensorNameBytes = 256 * sizeof(char);
	private const int SensorUnitBytes = 8 * sizeof(char);
	private const int SensorDigitsOffset = SensorNameBytes + SensorUnitBytes;
	private const int SensorValueOffset = SensorDigitsOffset + sizeof(uint);
	private const int SensorSize = SensorValueOffset + sizeof(double);

	private const int DataOffset = HeaderSize;
	private const int SensorsOffset = DataOffset + (MaxRecords * RecordSize);

	/// <summary>The number of bytes a complete block occupies.</summary>
	public const int TotalSize = SensorsOffset + (MaxSensors * SensorSize);

	public static GpuZReadResult Parse(ReadOnlySpan<byte> memory)
	{
		if (memory.Length < TotalSize)
		{
			throw new ArgumentException($"GPU-Z shared memory is {memory.Length} bytes; expected at least {TotalSize}.",
				nameof(memory));
		}

		if (BinaryPrimitives.ReadInt32LittleEndian(memory[BusyOffset..]) != 0)
		{
			return GpuZReadResult.Busy;
		}

		var data = new List<GpuZDataRecord>();
		for (var i = 0; i < MaxRecords; i++)
		{
			var record = memory.Slice(DataOffset + (i * RecordSize), RecordSize);
			var key = ReadString(record[..RecordKeyBytes]);
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

			data.Add(new GpuZDataRecord(key, ReadString(record.Slice(RecordKeyBytes, RecordValueBytes))));
		}

		var sensors = new List<GpuZSensorRecord>();
		for (var i = 0; i < MaxSensors; i++)
		{
			var sensor = memory.Slice(SensorsOffset + (i * SensorSize), SensorSize);
			var name = ReadString(sensor[..SensorNameBytes]);
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			sensors.Add(new GpuZSensorRecord(
				name,
				ReadUnit(sensor.Slice(SensorNameBytes, SensorUnitBytes)),
				(int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(sensor[SensorDigitsOffset..]), 15),
				BinaryPrimitives.ReadDoubleLittleEndian(sensor[SensorValueOffset..])));
		}

		return GpuZReadResult.Of(new GpuZSnapshot(data, sensors));
	}

	/// <summary>
	/// Reads a sensor's unit. GPU-Z publishes percent units printf-escaped as <c>%%</c> (GPU Load reads
	/// U+0025 U+0025), which Macro Deck would show as a doubled percent sign.
	/// </summary>
	private static string ReadUnit(ReadOnlySpan<byte> field)
		=> ReadString(field).Replace("%%", "%", StringComparison.Ordinal);

	/// <summary>Reads a fixed-size, NUL-terminated UTF-16 field.</summary>
	private static string ReadString(ReadOnlySpan<byte> field)
	{
		var text = Encoding.Unicode.GetString(field);
		var end = text.IndexOf('\0', StringComparison.Ordinal);
		return (end < 0 ? text : text[..end]).Trim();
	}
}
