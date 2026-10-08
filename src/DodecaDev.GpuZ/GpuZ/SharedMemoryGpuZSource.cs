using System.IO.MemoryMappedFiles;

namespace DodecaDev.GpuZ.GpuZ;

/// <summary>Reads GPU-Z's named shared memory. GPU-Z only exists on Windows; elsewhere it is never running.</summary>
public sealed class SharedMemoryGpuZSource : IGpuZSource
{
	private readonly byte[] _buffer = new byte[GpuZSharedMemoryLayout.TotalSize];

	public GpuZReadResult Read()
	{
		if (!OperatingSystem.IsWindows())
		{
			return GpuZReadResult.NotRunning;
		}

		try
		{
			using var map = MemoryMappedFile.OpenExisting(GpuZSharedMemoryLayout.MapName, MemoryMappedFileRights.Read);
			using var view = map.CreateViewAccessor(0, GpuZSharedMemoryLayout.TotalSize, MemoryMappedFileAccess.Read);

			// Called only from the monitor's single loop, so the shared buffer is never read concurrently.
			view.ReadArray(0, _buffer, 0, _buffer.Length);
		}
		catch (FileNotFoundException)
		{
			return GpuZReadResult.NotRunning;
		}

		return GpuZSharedMemoryLayout.Parse(_buffer);
	}
}
