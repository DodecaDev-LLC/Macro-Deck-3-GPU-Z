namespace DodecaDev.GpuZ.Variables;

/// <summary>
/// An extra catalog entry offered beside every sensor whose unit is <see cref="FromUnit"/>, such as
/// <c>Memory Used (Dedicated) (GB)</c> next to the MB reading. Bindings have no template to do the maths
/// in, so the conversion happens here.
/// </summary>
/// <param name="Code">Persisted in the catalog id and, as a suffix, in the variable name. Never change one.</param>
/// <param name="DecimalPlaces">Digits to show; <c>null</c> keeps the sensor's own precision.</param>
public sealed record UnitConversion(string Code, string FromUnit, string ToUnit, Func<double, double> Convert,
	int? DecimalPlaces)
{
	/// <summary>GPU-Z's MB are binary: an 8192 MB card is an 8 GB card.</summary>
	public static readonly IReadOnlyList<UnitConversion> All =
	[
		new("gb", "MB", "GB", mb => mb / 1024, 2),
		new("gb_s", "MB/s", "GB/s", mbPerSecond => mbPerSecond / 1024, 2),
		new("ghz", "MHz", "GHz", mhz => mhz / 1000, 2),
		new("f", "°C", "°F", celsius => (celsius * 9 / 5) + 32, null),
	];

	public static IEnumerable<UnitConversion> From(string unit)
		=> All.Where(conversion => conversion.FromUnit == unit);

	public static UnitConversion? ByCode(string code)
		=> All.FirstOrDefault(conversion => conversion.Code == code);
}
