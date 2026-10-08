using System.Globalization;
using DodecaDev.GpuZ.GpuZ;
using DodecaDev.GpuZ.Variables;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace DodecaDev.GpuZ;

/// <summary>
/// Exposes GPU-Z's shared memory as variables. GPU-Z publishes whatever its card and driver support, so
/// readings are a browsable catalog (<c>Card info</c> and <c>Sensors</c>) rather than a fixed list: the user
/// binds only what they need, and each binding becomes an ordinary <c>{{ vars.gpu_z_... }}</c> variable.
/// </summary>
public sealed class PluginIntegration : IPluginIntegration, IVariableProvider
{
	private static readonly TimeSpan SensorRefreshInterval = GpuZMonitor.RefreshInterval;

	private readonly GpuZMonitor _monitor;
	private readonly IPluginCatalogNotifier _catalogNotifier;

	public PluginIntegration(GpuZMonitor monitor, IPluginCatalogNotifier catalogNotifier)
	{
		_monitor = monitor;
		_catalogNotifier = catalogNotifier;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("gpu_z_is_running", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2)) with
		{
			Id = GpuZVariableIds.IsRunning,
			DisplayName = Strings.Variables.IsRunning.Name(),
			Description = Strings.Variables.IsRunning.Description(),
		},
	];

	public bool SupportsCatalog => true;

	public bool SupportsSearch => true;

	public string CatalogName => "GPU-Z";

	/// <summary>Runs again after reconnects; <see cref="GpuZMonitor.Start"/> is idempotent.</summary>
	public Task InitializeAsync(IIntegrationContext context)
	{
		_monitor.ReadingsChanged -= OnReadingsChanged;
		_monitor.ReadingsChanged += OnReadingsChanged;
		_monitor.Start();

		// The host may still hold a catalog from before this session (an earlier build, or GPU-Z not
		// running when it was last browsed).
		_catalogNotifier.CatalogChanged(CapabilityKinds.Variables, reason: "plugin initialized");
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		_monitor.ReadingsChanged -= OnReadingsChanged;
		return _monitor.StopAsync();
	}

	private void OnReadingsChanged()
		=> _catalogNotifier.CatalogChanged(CapabilityKinds.Variables, reason: "GPU-Z readings changed");

	public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
	{
		var snapshot = _monitor.Current;
		IEnumerable<VariableDefinition> items;

		if (!string.IsNullOrWhiteSpace(query.Search))
		{
			items = Leaves(snapshot, query.ParentId).Where(item =>
				item.DisplayName.Literal?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true
				|| item.Name?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true);
		}
		else if (query.ParentId is null)
		{
			items =
			[
				Container(GpuZVariableIds.CardInfoContainer, Strings.Catalog.CardInfo()),
				Container(GpuZVariableIds.SensorsContainer, Strings.Catalog.Sensors()),
			];
		}
		else
		{
			items = Leaves(snapshot, query.ParentId);
		}

		return ValueTask.FromResult(Page(items.ToList(), query));
	}

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (!GpuZVariableIds.TryParse(localId, out var kind, out var gpuZName, out var conversion))
		{
			return ValueTask.FromResult<VariableDefinition?>(null);
		}

		// A reading GPU-Z is not publishing right now (not running, other card) still resolves, so the
		// binding reads as unavailable and resumes on its own instead of becoming a broken reference.
		var snapshot = _monitor.Current;
		return ValueTask.FromResult<VariableDefinition?>(kind switch
		{
			GpuZVariableKind.CardInfo => CardInfo(gpuZName),
			_ when conversion is not null => ConvertedSensor(gpuZName, conversion, snapshot?.FindSensor(gpuZName)),
			_ => Sensor(gpuZName, snapshot?.FindSensor(gpuZName)),
		});
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var snapshot = _monitor.Current;

		if (localId == GpuZVariableIds.IsRunning)
		{
			return ValueTask.FromResult(VariableReading.Of(snapshot is not null));
		}

		if (snapshot is null || !GpuZVariableIds.TryParse(localId, out var kind, out var gpuZName, out var conversion))
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		return ValueTask.FromResult(kind switch
		{
			GpuZVariableKind.CardInfo when snapshot.FindData(gpuZName) is { } record
				=> VariableReading.Of(record.Value),
			GpuZVariableKind.Sensor when snapshot.FindSensor(gpuZName) is { } sensor && double.IsFinite(sensor.Value)
				=> conversion switch
				{
					null => VariableReading.Of(sensor.Value),
					// The card reports this sensor in another unit now: converting it would be a wrong number.
					_ when sensor.Unit != conversion.FromUnit => VariableReading.Unavailable,
					_ => VariableReading.Of(conversion.Convert(sensor.Value)),
				},
			_ => VariableReading.Unavailable,
		});
	}

	private static IEnumerable<VariableDefinition> Leaves(GpuZSnapshot? snapshot, string? parentId)
	{
		if (snapshot is null)
		{
			return [];
		}

		var cardInfo = snapshot.Data.Select(record => CardInfo(record.Key));
		var sensors = snapshot.Sensors.SelectMany(sensor => UnitConversion.From(sensor.Unit)
			.Select(conversion => ConvertedSensor(sensor.Name, conversion, sensor))
			.Prepend(Sensor(sensor.Name, sensor)));

		return parentId switch
		{
			GpuZVariableIds.CardInfoContainer => cardInfo,
			GpuZVariableIds.SensorsContainer => sensors,
			null => cardInfo.Concat(sensors),
			_ => [],
		};
	}

	private static VariableDefinition Container(string id, LocalizedText displayName)
		=> VariableDefinition.OnDemand(id, VariableType.Text) with
		{
			Name = GpuZVariableIds.NamePrefix + id.Replace('-', '_'),
			DisplayName = displayName,
			IsContainer = true,
			IsBindable = false,
		};

	private static VariableDefinition CardInfo(string key)
		=> VariableDefinition.OnDemand(GpuZVariableIds.ForCardInfo(key), VariableType.Text) with
		{
			Name = GpuZVariableIds.ToVariableName(key),
			DisplayName = LocalizedText.FromLiteral(key),
			ParentId = GpuZVariableIds.CardInfoContainer,
		};

	/// <param name="current">The sensor's latest reading, which supplies its unit and precision when known.</param>
	private static VariableDefinition Sensor(string name, GpuZSensorRecord? current)
	{
		var definition = VariableDefinition.OnDemand(GpuZVariableIds.ForSensor(name), VariableType.Numeric) with
		{
			Name = GpuZVariableIds.ToVariableName(name),
			DisplayName = LocalizedText.FromLiteral(name),
			ParentId = GpuZVariableIds.SensorsContainer,
			RefreshInterval = SensorRefreshInterval,
		};

		if (current is null)
		{
			return definition;
		}

		var unit = string.IsNullOrWhiteSpace(current.Unit) ? null : current.Unit;
		return definition with
		{
			Unit = unit,
			SemanticKind = unit == "%" ? VariableSemanticKinds.Percentage : null,
			DecimalPlaces = current.Digits,
			Attributes = new Dictionary<string, string>
			{
				["digits"] = current.Digits.ToString(CultureInfo.InvariantCulture),
			},
		};
	}

	/// <param name="current">The sensor's latest reading, which supplies its precision when known.</param>
	private static VariableDefinition ConvertedSensor(string name, UnitConversion conversion, GpuZSensorRecord? current)
	{
		var decimals = conversion.DecimalPlaces ?? current?.Digits;
		return VariableDefinition.OnDemand(GpuZVariableIds.ForSensor(name, conversion), VariableType.Numeric) with
		{
			Name = GpuZVariableIds.ToVariableName(name, conversion),
			DisplayName = LocalizedText.FromLiteral($"{name} ({conversion.ToUnit})"),
			ParentId = GpuZVariableIds.SensorsContainer,
			RefreshInterval = SensorRefreshInterval,
			Unit = conversion.ToUnit,
			DecimalPlaces = decimals,
			Attributes = decimals is { } digits
				? new Dictionary<string, string> { ["digits"] = digits.ToString(CultureInfo.InvariantCulture) }
				: null,
		};
	}

	private static VariableCatalogPage Page(List<VariableDefinition> items, VariableCatalogQuery query)
	{
		var offset = int.TryParse(query.ContinuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
			? Math.Clamp(parsed, 0, items.Count)
			: 0;
		var size = Math.Clamp(query.PageSize, 1, ProtocolLimits.MaxVariableCatalogPageSize);
		var end = Math.Min(offset + size, items.Count);

		return new VariableCatalogPage
		{
			Items = items.Skip(offset).Take(end - offset).ToList(),
			ContinuationToken = end < items.Count ? end.ToString(CultureInfo.InvariantCulture) : null,
		};
	}
}
