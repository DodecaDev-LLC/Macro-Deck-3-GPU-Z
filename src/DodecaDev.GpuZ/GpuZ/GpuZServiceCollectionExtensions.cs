using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DodecaDev.GpuZ.GpuZ;

public static class GpuZServiceCollectionExtensions
{
	/// <summary>
	/// Registers the GPU-Z reader and monitor. <c>TryAdd</c>, so a test can register a fake
	/// <see cref="IGpuZSource"/> first.
	/// </summary>
	public static IServiceCollection AddGpuZ(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<IGpuZSource, SharedMemoryGpuZSource>();
		services.TryAddSingleton<GpuZMonitor>();
		return services;
	}
}
