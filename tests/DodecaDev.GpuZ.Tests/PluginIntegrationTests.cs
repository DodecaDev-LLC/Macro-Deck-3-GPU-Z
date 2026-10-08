using DodecaDev.GpuZ.GpuZ;
using DodecaDev.GpuZ.Variables;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DodecaDev.GpuZ.Tests;

/// <summary>
/// Behaviour tests through <see cref="PluginTestHarness"/>: the plugin's own capability handlers run,
/// but nothing crosses a socket. GPU-Z is replaced by <see cref="FakeGpuZSource"/>.
/// </summary>
[TestFixture]
public sealed class PluginIntegrationTests
{
	private static readonly string GpuTemperature = GpuZVariableIds.ForSensor("GPU Temperature");
	private static readonly string CardName = GpuZVariableIds.ForCardInfo("CardName");

	private FakeGpuZSource _gpuZ = null!;
	private RecordingCatalogNotifier _catalog = null!;

	[SetUp]
	public void SetUp()
	{
		_gpuZ = new FakeGpuZSource();
		_catalog = new RecordingCatalogNotifier();
	}

	private PluginTestHarness CreateHarness() =>
		PluginTestHarness.Create(builder =>
		{
			builder.Services.AddSingleton<IGpuZSource>(_gpuZ);
			builder.Services.AddGpuZ();
			builder
				.UseLocalization(Strings.LocalizationCatalog)
				.RegisterIntegration<PluginIntegration>();
			builder.Services.AddSingleton<IPluginCatalogNotifier>(_catalog);
		});

	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = CreateHarness();

		Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

	[Test]
	public async Task Sensors_read_as_plain_numbers()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot(temperature: 64.5));
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var reading = await ReadAsync(harness, GpuTemperature);

		Assert.That(reading.Kind, Is.EqualTo("number"));
		Assert.That(reading.Number, Is.EqualTo(64.5));
	}

	[Test]
	public async Task Card_info_reads_as_text()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot());
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var reading = await ReadAsync(harness, CardName);

		Assert.That(reading.Text, Is.EqualTo("NVIDIA GeForce RTX 4070"));
	}

	[Test]
	public async Task Everything_is_unavailable_while_GPU_Z_is_not_running()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		Assert.That((await ReadAsync(harness, GpuZVariableIds.IsRunning)).Boolean, Is.False);
		Assert.That((await ReadAsync(harness, GpuTemperature)).Kind, Is.EqualTo(VariableValueDto.Unavailable.Kind));
		Assert.That((await ReadAsync(harness, CardName)).Kind, Is.EqualTo(VariableValueDto.Unavailable.Kind));
	}

	[Test]
	public async Task A_sensor_the_card_does_not_have_is_unavailable()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot());
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var reading = await ReadAsync(harness, GpuZVariableIds.ForSensor("Hot Spot"));

		Assert.That(reading.Kind, Is.EqualTo(VariableValueDto.Unavailable.Kind));
	}

	[Test]
	public async Task Readings_pick_up_GPU_Z_starting_after_the_plugin()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();
		Assert.That((await ReadAsync(harness, GpuZVariableIds.IsRunning)).Boolean, Is.False);

		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot(temperature: 70));
		harness.Clock.Advance(GpuZMonitor.RefreshInterval);

		var monitor = harness.Services.GetRequiredService<GpuZMonitor>();
		await Wait.UntilAsync(() => monitor.Current is not null, because: "the monitor refreshes once per interval");
		Assert.That((await ReadAsync(harness, GpuZVariableIds.IsRunning)).Boolean, Is.True);
		Assert.That((await ReadAsync(harness, GpuTemperature)).Number, Is.EqualTo(70));
	}

	[Test]
	public async Task The_host_is_told_the_catalog_changed_when_GPU_Z_starts()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();
		var before = _catalog.Kinds.Count;

		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot());
		harness.Clock.Advance(GpuZMonitor.RefreshInterval);

		await Wait.UntilAsync(() => _catalog.Kinds.Count > before, because: "GPU-Z's readings appeared");
		Assert.That(_catalog.Kinds.Skip(before), Is.All.EqualTo(CapabilityKinds.Variables));
	}

	[Test]
	public async Task Unchanged_readings_do_not_renotify_the_host()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot(temperature: 60));
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();
		var before = _catalog.Kinds.Count;

		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot(temperature: 75));
		harness.Clock.Advance(GpuZMonitor.RefreshInterval);
		var monitor = harness.Services.GetRequiredService<GpuZMonitor>();
		await Wait.UntilAsync(() => monitor.Current?.FindSensor("GPU Temperature")?.Value == 75,
			because: "the new value was read");

		Assert.That(_catalog.Kinds, Has.Count.EqualTo(before));
	}

	[Test]
	public async Task A_busy_read_keeps_the_previous_readings()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot(temperature: 61));
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		_gpuZ.Returns(GpuZReadResult.Busy);
		harness.Clock.Advance(GpuZMonitor.RefreshInterval);

		Assert.That((await ReadAsync(harness, GpuZVariableIds.IsRunning)).Boolean, Is.True);
		Assert.That((await ReadAsync(harness, GpuTemperature)).Number, Is.EqualTo(61));
	}

	[Test]
	public async Task The_catalog_root_has_card_info_and_sensors_folders()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var page = await DiscoverAsync(harness);

		Assert.That(page.Items.Select(i => i.Id),
			Is.EqualTo(new[] { GpuZVariableIds.CardInfoContainer, GpuZVariableIds.SensorsContainer }));
		Assert.That(page.Items.All(i => i.IsContainer && !i.IsBindable), Is.True);
	}

	[Test]
	public async Task A_sensor_carries_its_unit_and_digits_as_metadata()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot());
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var page = await DiscoverAsync(harness, parentId: GpuZVariableIds.SensorsContainer);
		var temperature = page.Items.Single(i => i.Id == GpuTemperature);

		Assert.That(temperature.Name, Is.EqualTo("gpu_z_gpu_temperature"));
		Assert.That(temperature.Type, Is.EqualTo("numeric").IgnoreCase);
		Assert.That(temperature.Unit, Is.EqualTo("°C"));
		Assert.That(temperature.DecimalPlaces, Is.EqualTo(1));
		Assert.That(temperature.Attributes!["digits"], Is.EqualTo("1"));
		Assert.That(temperature.IsBindable, Is.True);
	}

	[Test]
	public async Task Card_info_lists_GPU_Z_records()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot());
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var page = await DiscoverAsync(harness, parentId: GpuZVariableIds.CardInfoContainer);

		Assert.That(page.Items.Select(i => i.Name), Is.EqualTo(new[] { "gpu_z_cardname", "gpu_z_driverversion" }));
	}

	[Test]
	public async Task Search_finds_readings_across_both_folders()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot());
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var page = await DiscoverAsync(harness, search: "gpu");

		Assert.That(page.Items.Select(i => i.Id), Does.Contain(GpuTemperature).And.Contain(CardName));
		Assert.That(page.Items.Any(i => i.IsContainer), Is.False);
	}

	[Test]
	public async Task The_catalog_pages_with_a_continuation_token()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot());
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var all = await DiscoverAsync(harness, parentId: GpuZVariableIds.SensorsContainer);
		var paged = new List<string?>();
		string? token = null;
		do
		{
			var page = await DiscoverAsync(harness, parentId: GpuZVariableIds.SensorsContainer, pageSize: 2,
				continuationToken: token);
			Assert.That(page.Items, Has.Count.LessThanOrEqualTo(2));
			paged.AddRange(page.Items.Select(i => i.Id));
			token = page.ContinuationToken;
		}
		while (token is not null);

		Assert.That(paged, Is.EqualTo(all.Items.Select(i => i.Id)));
	}

	[TestCase("Memory Used (Dedicated)", "gb", 6144, 6.0, "GB", "gpu_z_memory_used__dedicated__gb")]
	[TestCase("GPU Clock", "ghz", 2475.0, 2.475, "GHz", "gpu_z_gpu_clock_ghz")]
	[TestCase("GPU Temperature", "f", 64.5, 148.1, "°F", "gpu_z_gpu_temperature_f")]
	public async Task Converted_sensors_are_offered_beside_the_original(string sensor, string code, double raw,
		double expected, string unit, string name)
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot());
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();
		var conversion = UnitConversion.ByCode(code)!;
		var id = GpuZVariableIds.ForSensor(sensor, conversion);

		var items = (await DiscoverAsync(harness, parentId: GpuZVariableIds.SensorsContainer)).Items.ToList();
		var original = items.FindIndex(i => i.Id == GpuZVariableIds.ForSensor(sensor));
		var converted = items.FindIndex(i => i.Id == id);

		Assert.That(converted, Is.EqualTo(original + 1), "listed right after the original");
		Assert.That(items[converted].Name, Is.EqualTo(name));
		Assert.That(items[converted].Unit, Is.EqualTo(unit));
		Assert.That((await ReadAsync(harness, GpuZVariableIds.ForSensor(sensor))).Number, Is.EqualTo(raw));
		Assert.That((await ReadAsync(harness, id)).Number, Is.EqualTo(expected).Within(1e-9));
	}

	[Test]
	public async Task Sensors_without_a_conversion_get_no_extra_entry()
	{
		_gpuZ.Returns(FakeGpuZSource.SampleSnapshot());
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var items = (await DiscoverAsync(harness, parentId: GpuZVariableIds.SensorsContainer)).Items;

		Assert.That(items.Count(i => i.Id?.StartsWith(GpuZVariableIds.ForSensor("GPU Load"), StringComparison.Ordinal) == true),
			Is.EqualTo(1));
	}

	[Test]
	public async Task A_converted_sensor_is_unavailable_when_GPU_Z_reports_another_unit()
	{
		_gpuZ.Returns(new GpuZSnapshot([], [new GpuZSensorRecord("GPU Clock", "kHz", 0, 2475000)]));
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var reading = await ReadAsync(harness, GpuZVariableIds.ForSensor("GPU Clock", UnitConversion.ByCode("ghz")!));

		Assert.That(reading.Kind, Is.EqualTo(VariableValueDto.Unavailable.Kind));
	}

	[Test]
	public async Task A_converted_sensor_resolves_while_GPU_Z_is_not_running()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Variables.ResolveAsync(
			GpuZVariableIds.ForSensor("Memory Used (Dedicated)", UnitConversion.ByCode("gb")!));
		var definition = outcome.DataAs<VariableResolveResult>()!.Definition;

		Assert.That(definition!.Name, Is.EqualTo("gpu_z_memory_used__dedicated__gb"));
		Assert.That(definition.Unit, Is.EqualTo("GB"));
	}

	[Test]
	public async Task A_bound_sensor_still_resolves_while_GPU_Z_is_not_running()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Variables.ResolveAsync(GpuTemperature);
		var definition = outcome.DataAs<VariableResolveResult>()!.Definition;

		Assert.That(definition, Is.Not.Null);
		Assert.That(definition!.Name, Is.EqualTo("gpu_z_gpu_temperature"));
	}

	[TestCase("not-a-gpu-z-id")]
	[TestCase("sensor.")]
	[TestCase("sensor.GPU%20Clock:unknown")]
	[TestCase(GpuZVariableIds.SensorsContainer)]
	public async Task An_invalid_id_does_not_resolve(string id)
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Variables.ResolveAsync(id);

		Assert.That(outcome.DataAs<VariableResolveResult>()?.Definition, Is.Null);
	}

	private static async Task<VariableValueDto> ReadAsync(PluginTestHarness harness, string localId)
	{
		var outcome = await harness.Variables.GetAsync(localId);
		Assert.That(outcome.Succeeded, Is.True, outcome.ToString());
		return outcome.DataAs<VariableReadingDto>()!.Value;
	}

	private static async Task<VariableCatalogPageResult> DiscoverAsync(PluginTestHarness harness,
		string? parentId = null, string? search = null, string? continuationToken = null, int pageSize = 100)
	{
		var outcome = await harness.Variables.DiscoverAsync(parentId, search, continuationToken, pageSize);
		Assert.That(outcome.Succeeded, Is.True, outcome.ToString());
		return outcome.DataAs<VariableCatalogPageResult>()!;
	}
}

/// <summary>
/// The localization set is generated from <c>Localization/*.resx</c>, so these guard the wiring rather
/// than any wording: a missing catalog registration leaves every label showing its raw key.
/// </summary>
[TestFixture]
public sealed class LocalizationTests
{
	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.dodecadev.gpu-z"));
	}

	[Test]
	public void English_is_the_default_culture()
	{
		Assert.That(Strings.LocalizationCatalog.DefaultCulture, Is.EqualTo("en"));
		Assert.That(Strings.LocalizationCatalog.Cultures, Does.Contain("en"));
	}

	[Test]
	public void Every_key_the_default_culture_declares_resolves_to_text()
	{
		foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
		{
			Assert.That(Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text), Is.True);
			Assert.That(text, Is.Not.Empty);
		}
	}
}
