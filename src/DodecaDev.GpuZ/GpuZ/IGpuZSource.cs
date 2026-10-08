namespace DodecaDev.GpuZ.GpuZ;

public enum GpuZReadStatus
{
	/// <summary>The snapshot was read.</summary>
	Read,

	/// <summary>GPU-Z's shared memory does not exist: GPU-Z is not running.</summary>
	NotRunning,

	/// <summary>GPU-Z was writing while we read; the previous snapshot is still the best one.</summary>
	Busy,
}

public readonly record struct GpuZReadResult(GpuZReadStatus Status, GpuZSnapshot? Snapshot)
{
	public static GpuZReadResult NotRunning { get; } = new(GpuZReadStatus.NotRunning, null);

	public static GpuZReadResult Busy { get; } = new(GpuZReadStatus.Busy, null);

	public static GpuZReadResult Of(GpuZSnapshot snapshot) => new(GpuZReadStatus.Read, snapshot);
}

/// <summary>Where GPU-Z readings come from. Swapped for a fake in tests.</summary>
public interface IGpuZSource
{
	GpuZReadResult Read();
}
