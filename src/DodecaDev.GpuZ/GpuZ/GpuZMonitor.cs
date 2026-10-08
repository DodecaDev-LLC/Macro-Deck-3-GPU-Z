using Serilog;

namespace DodecaDev.GpuZ.GpuZ;

/// <summary>
/// Keeps the latest GPU-Z snapshot in memory, so the host's variable polls answer from cached state
/// instead of opening shared memory on every read. GPU-Z itself refreshes about once a second.
/// </summary>
public sealed class GpuZMonitor : IAsyncDisposable
{
	public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

	private readonly IGpuZSource _source;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();

	private GpuZSnapshot? _current;
	private bool? _wasRunning;
	private string? _shape;
	private CancellationTokenSource? _stopping;
	private Task? _loop;

	public GpuZMonitor(IGpuZSource source, TimeProvider time, ILogger logger)
	{
		_source = source;
		_time = time;
		_logger = logger.ForContext<GpuZMonitor>();
	}

	/// <summary>
	/// Raised from the refresh loop when the set of readings GPU-Z offers changes (GPU-Z started or stopped,
	/// a sensor appeared, a unit changed), so the variable catalog the host shows is out of date.
	/// </summary>
	public event Action? ReadingsChanged;

	/// <summary>The latest snapshot, or <c>null</c> while GPU-Z is not running.</summary>
	public GpuZSnapshot? Current => Volatile.Read(ref _current);

	/// <summary>Reads once immediately, then keeps refreshing until <see cref="StopAsync"/>. Idempotent.</summary>
	public void Start()
	{
		lock (_gate)
		{
			if (_loop is not null)
			{
				return;
			}

			Refresh();
			_stopping = new CancellationTokenSource();
			_loop = RunAsync(_stopping.Token);
		}
	}

	public async Task StopAsync()
	{
		Task? loop;
		CancellationTokenSource? stopping;
		lock (_gate)
		{
			(loop, stopping) = (_loop, _stopping);
			(_loop, _stopping) = (null, null);
		}

		if (stopping is null || loop is null)
		{
			return;
		}

		await stopping.CancelAsync().ConfigureAwait(false);
		await loop.ConfigureAwait(false);
		stopping.Dispose();
	}

	public ValueTask DisposeAsync() => new(StopAsync());

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(RefreshInterval, _time, cancellationToken).ConfigureAwait(false);
				Refresh();
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private void Refresh()
	{
		GpuZReadResult result;
		try
		{
			result = _source.Read();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			_logger.Warning(ex, "Could not read GPU-Z shared memory; keeping the previous readings");
			return;
		}

		switch (result.Status)
		{
			case GpuZReadStatus.Read:
				Volatile.Write(ref _current, result.Snapshot);
				ReportRunning(true);
				ReportShape(result.Snapshot);
				break;
			case GpuZReadStatus.NotRunning:
				Volatile.Write(ref _current, null);
				ReportRunning(false);
				ReportShape(null);
				break;
			case GpuZReadStatus.Busy:
				break;
		}
	}

	private void ReportShape(GpuZSnapshot? snapshot)
	{
		var shape = snapshot is null
			? ""
			: string.Join('\n', snapshot.Data.Select(d => d.Key)
				.Concat(snapshot.Sensors.Select(sensor => $"{sensor.Name}\t{sensor.Unit}")));
		if (shape == _shape)
		{
			return;
		}

		var first = _shape is null;
		_shape = shape;
		if (!first)
		{
			ReadingsChanged?.Invoke();
		}
	}

	private void ReportRunning(bool running)
	{
		if (_wasRunning == running)
		{
			return;
		}

		_wasRunning = running;
		if (running)
		{
			_logger.Information("Reading GPU-Z shared memory");
		}
		else
		{
			_logger.Warning("GPU-Z is not running; its variables are unavailable until it starts");
		}
	}
}
