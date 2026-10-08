using System.Buffers.Binary;
using System.Text;
using DodecaDev.GpuZ.GpuZ;
using DodecaDev.GpuZ.Variables;
using NUnit.Framework;

namespace DodecaDev.GpuZ.Tests;

/// <summary>
/// Guards the byte layout of GPU-Z's packed <c>GPUZ_SH_MEM</c> struct: a wrong offset reads garbage
/// rather than failing, so these build a block by hand at the documented offsets.
/// </summary>
[TestFixture]
public sealed class GpuZSharedMemoryLayoutTests
{
	private const int RecordSize = 1024;
	private const int SensorSize = 540;
	private const int SensorsOffset = 12 + (128 * RecordSize);

	[Test]
	public void The_block_is_the_size_of_GPU_Zs_struct()
	{
		Assert.That(GpuZSharedMemoryLayout.TotalSize, Is.EqualTo(12 + (128 * 1024) + (128 * 540)));
	}

	[Test]
	public void Records_and_sensors_are_read_at_their_offsets()
	{
		var memory = new byte[GpuZSharedMemoryLayout.TotalSize];
		WriteRecord(memory, 0, "CardName", "Radeon RX 7800 XT");
		WriteRecord(memory, 2, "BIOSVersion", new string('9', 200));   // longer than 128 chars: the full field
		WriteSensor(memory, 0, "GPU Temperature", "°C", 1, 58.25);
		WriteSensor(memory, 5, "Board Power Draw", "W", 2, 211.5);

		var result = GpuZSharedMemoryLayout.Parse(memory);

		Assert.That(result.Status, Is.EqualTo(GpuZReadStatus.Read));
		var snapshot = result.Snapshot!;
		Assert.That(snapshot.Data.Select(d => d.Key), Is.EqualTo(new[] { "CardName", "BIOSVersion" }));
		Assert.That(snapshot.FindData("CardName")!.Value, Is.EqualTo("Radeon RX 7800 XT"));
		Assert.That(snapshot.FindData("BIOSVersion")!.Value, Has.Length.EqualTo(200));
		Assert.That(snapshot.FindSensor("GPU Temperature"),
			Is.EqualTo(new GpuZSensorRecord("GPU Temperature", "°C", 1, 58.25)));
		Assert.That(snapshot.FindSensor("Board Power Draw"),
			Is.EqualTo(new GpuZSensorRecord("Board Power Draw", "W", 2, 211.5)));
	}

	[Test]
	public void A_printf_escaped_percent_unit_reads_as_one_percent_sign()
	{
		var memory = new byte[GpuZSharedMemoryLayout.TotalSize];
		WriteSensor(memory, 0, "GPU Load", "%%", 0, 42);

		var sensor = GpuZSharedMemoryLayout.Parse(memory).Snapshot!.FindSensor("GPU Load");

		Assert.That(sensor!.Unit, Is.EqualTo("%"));
	}

	[Test]
	public void A_block_GPU_Z_is_writing_reads_as_busy()
	{
		var memory = new byte[GpuZSharedMemoryLayout.TotalSize];
		BinaryPrimitives.WriteInt32LittleEndian(memory.AsSpan(4), 1);

		Assert.That(GpuZSharedMemoryLayout.Parse(memory).Status, Is.EqualTo(GpuZReadStatus.Busy));
	}

	[Test]
	public void A_truncated_block_is_rejected()
	{
		Assert.Throws<ArgumentException>(() => GpuZSharedMemoryLayout.Parse(new byte[100]));
	}

	[TestCase("GPU Temperature", "gpu_z_gpu_temperature")]
	[TestCase("Memory Used (Dedicated)", "gpu_z_memory_used__dedicated_")]
	[TestCase("CardName", "gpu_z_cardname")]
	[TestCase("Monitor_1_AspectRatio", "gpu_z_monitor_1_aspectratio")]
	public void Variable_names_follow_the_Macro_Deck_2_convention(string gpuZName, string expected)
	{
		Assert.That(GpuZVariableIds.ToVariableName(gpuZName), Is.EqualTo(expected));
	}

	[TestCase("GPU Temperature")]
	[TestCase("Memory Used (Dedicated)")]
	[TestCase("CPU::Core 0 % / sec")]
	public void Catalog_ids_round_trip_and_never_contain_whitespace_or_double_colons(string gpuZName)
	{
		var id = GpuZVariableIds.ForSensor(gpuZName);

		Assert.That(id, Does.Not.Contain(" ").And.Not.Contain("::"));
		Assert.That(GpuZVariableIds.TryParse(id, out var kind, out var parsed), Is.True);
		Assert.That(kind, Is.EqualTo(GpuZVariableKind.Sensor));
		Assert.That(parsed, Is.EqualTo(gpuZName));
	}

	private static void WriteRecord(byte[] memory, int index, string key, string value)
	{
		var offset = 12 + (index * RecordSize);
		Encoding.Unicode.GetBytes(key).CopyTo(memory, offset);
		Encoding.Unicode.GetBytes(value).CopyTo(memory, offset + 512);
	}

	private static void WriteSensor(byte[] memory, int index, string name, string unit, uint digits, double value)
	{
		var offset = SensorsOffset + (index * SensorSize);
		Encoding.Unicode.GetBytes(name).CopyTo(memory, offset);
		Encoding.Unicode.GetBytes(unit).CopyTo(memory, offset + 512);
		BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(offset + 528), digits);
		BinaryPrimitives.WriteDoubleLittleEndian(memory.AsSpan(offset + 532), value);
	}
}
