using MacroDeck.Plugin.Hosting.Integrations.HostApis;

namespace DodecaDev.GpuZ.Tests;

internal sealed class RecordingCatalogNotifier : IPluginCatalogNotifier
{
	private readonly Lock _gate = new();
	private readonly List<string> _kinds = [];

	public IReadOnlyList<string> Kinds
	{
		get
		{
			lock (_gate)
			{
				return [.. _kinds];
			}
		}
	}

	public void CatalogChanged(string kind, string? localId = null, string? reason = null)
	{
		lock (_gate)
		{
			_kinds.Add(kind);
		}
	}
}
