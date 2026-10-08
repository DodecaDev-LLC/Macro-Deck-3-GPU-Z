using DodecaDev.GpuZ.GpuZ;

namespace DodecaDev.GpuZ.Tests;

/// <summary>Stands in for GPU-Z's shared memory. Starts as "GPU-Z is not running".</summary>
internal sealed class FakeGpuZSource : IGpuZSource
{
	private readonly Lock _gate = new();
	private GpuZReadResult _next = GpuZReadResult.NotRunning;

	public GpuZReadResult Read()
	{
		lock (_gate)
		{
			return _next;
		}
	}

	public void Returns(GpuZReadResult result)
	{
		lock (_gate)
		{
			_next = result;
		}
	}

	public void Returns(GpuZSnapshot snapshot) => Returns(GpuZReadResult.Of(snapshot));

	public static GpuZSnapshot SampleSnapshot(double temperature = 64.5) => new(
		[
			new GpuZDataRecord("CardName", "NVIDIA GeForce RTX 4070"),
			new GpuZDataRecord("DriverVersion", "32.0.15.6094"),
		],
		[
			new GpuZSensorRecord("GPU Clock", "MHz", 1, 2475.0),
			new GpuZSensorRecord("GPU Temperature", "°C", 1, temperature),
			new GpuZSensorRecord("GPU Load", "%", 0, 37),
			new GpuZSensorRecord("Memory Used (Dedicated)", "MB", 0, 6144),
		]);
}
