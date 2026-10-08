namespace DodecaDev.GpuZ.GpuZ;

/// <summary>A card-information record from GPU-Z, such as <c>CardName</c> or <c>DriverVersion</c>.</summary>
public sealed record GpuZDataRecord(string Key, string Value);

/// <summary>A live sensor from GPU-Z, such as <c>GPU Temperature</c>.</summary>
/// <param name="Digits">The number of decimal places GPU-Z itself shows for the value.</param>
public sealed record GpuZSensorRecord(string Name, string Unit, int Digits, double Value);

/// <summary>One consistent read of GPU-Z's shared memory.</summary>
public sealed class GpuZSnapshot
{
	private readonly Dictionary<string, GpuZDataRecord> _dataByKey = new(StringComparer.Ordinal);
	private readonly Dictionary<string, GpuZSensorRecord> _sensorsByName = new(StringComparer.Ordinal);

	public GpuZSnapshot(IReadOnlyList<GpuZDataRecord> data, IReadOnlyList<GpuZSensorRecord> sensors)
	{
		Data = data;
		Sensors = sensors;

		// GPU-Z does not promise unique names; the first record wins, as it is the one GPU-Z lists first.
		foreach (var record in data)
		{
			_dataByKey.TryAdd(record.Key, record);
		}

		foreach (var sensor in sensors)
		{
			_sensorsByName.TryAdd(sensor.Name, sensor);
		}
	}

	public IReadOnlyList<GpuZDataRecord> Data { get; }

	public IReadOnlyList<GpuZSensorRecord> Sensors { get; }

	public GpuZDataRecord? FindData(string key) => _dataByKey.GetValueOrDefault(key);

	public GpuZSensorRecord? FindSensor(string name) => _sensorsByName.GetValueOrDefault(name);
}
