using System.Text.RegularExpressions;

namespace DodecaDev.GpuZ.Variables;

public enum GpuZVariableKind
{
	CardInfo,
	Sensor,
}

/// <summary>
/// Catalog ids and variable names. Both end up in users' profiles, so neither scheme may change once
/// shipped. Ids carry GPU-Z's own name URI-escaped (no whitespace, reversible), and a converted sensor
/// appends <c>:&lt;code&gt;</c> - unambiguous, because escaping always encodes a ':' in the name. Names follow
/// the Macro Deck 2 plugin's <c>gpu_z_&lt;name&gt;</c> convention so users recognise them.
/// </summary>
public static partial class GpuZVariableIds
{
	public const string NamePrefix = "gpu_z_";

	public const string CardInfoContainer = "card-info";
	public const string SensorsContainer = "sensors";
	public const string IsRunning = "is-running";

	private const string CardInfoPrefix = "info.";
	private const string SensorPrefix = "sensor.";
	private const char ConversionSeparator = ':';

	public static string ForCardInfo(string key) => CardInfoPrefix + Uri.EscapeDataString(key);

	public static string ForSensor(string name) => SensorPrefix + Uri.EscapeDataString(name);

	public static string ForSensor(string name, UnitConversion conversion)
		=> ForSensor(name) + ConversionSeparator + conversion.Code;

	public static bool TryParse(string localId, out GpuZVariableKind kind, out string gpuZName)
		=> TryParse(localId, out kind, out gpuZName, out var conversion) && conversion is null;

	/// <param name="conversion">The unit conversion a converted sensor's id names; <c>null</c> for the raw reading.</param>
	public static bool TryParse(string localId, out GpuZVariableKind kind, out string gpuZName,
		out UnitConversion? conversion)
	{
		(kind, var escaped) = localId switch
		{
			_ when localId.StartsWith(CardInfoPrefix, StringComparison.Ordinal)
				=> (GpuZVariableKind.CardInfo, localId[CardInfoPrefix.Length..]),
			_ when localId.StartsWith(SensorPrefix, StringComparison.Ordinal)
				=> (GpuZVariableKind.Sensor, localId[SensorPrefix.Length..]),
			_ => (default, ""),
		};

		conversion = null;
		var separator = escaped.LastIndexOf(ConversionSeparator);
		if (kind == GpuZVariableKind.Sensor && separator >= 0)
		{
			conversion = UnitConversion.ByCode(escaped[(separator + 1)..]);
			if (conversion is null)
			{
				gpuZName = "";
				return false;
			}

			escaped = escaped[..separator];
		}

		gpuZName = Uri.UnescapeDataString(escaped);
		return !string.IsNullOrWhiteSpace(gpuZName);
	}

	/// <summary><c>GPU Temperature</c> becomes <c>gpu_z_gpu_temperature</c>.</summary>
	public static string ToVariableName(string gpuZName)
		=> NamePrefix + NonAlphanumeric().Replace(gpuZName.ToLowerInvariant(), "_");

	/// <summary><c>Memory Used</c> in GB becomes <c>gpu_z_memory_used_gb</c>.</summary>
	public static string ToVariableName(string gpuZName, UnitConversion conversion)
		=> ToVariableName(gpuZName) + "_" + conversion.Code;

	[GeneratedRegex("[^a-z0-9]")]
	private static partial Regex NonAlphanumeric();
}
