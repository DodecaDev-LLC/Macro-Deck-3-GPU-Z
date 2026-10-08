# GPU-Z Plugin for Macro Deck 3

Live hardware readings from [GPU-Z](https://www.techpowerup.com/gpuz/) as Macro Deck variables: clocks,
temperatures, load, fan speed and power draw, plus your graphics card's details (name, driver, memory,
BIOS).

This is the Macro Deck 3 version of the
[Macro Deck 2 GPU-Z plugin](https://github.com/DodecaDev-LLC/Macro-Deck-GPU-Z-Plugin). It is not affiliated
with or endorsed by TechPowerUp.

## Requirements

- Windows (GPU-Z is Windows only)
- GPU-Z **running**. The plugin reads the shared memory GPU-Z publishes while it is open; nothing needs to
  be configured in GPU-Z itself.

## Using the variables

Open the variable browser in Macro Deck and expand **GPU-Z**:

| Folder | Contains | Type |
| --- | --- | --- |
| **Card info** | Everything GPU-Z lists about the card: `CardName`, `DriverVersion`, `MemSize`, `BIOSVersion`, ... | Text |
| **Sensors** | Every sensor GPU-Z shows for your card: `GPU Clock`, `GPU Temperature`, `GPU Load`, ... | Number |

Pick the readings you want. Each one becomes an ordinary variable named after GPU-Z's own name, for
example `GPU Temperature` becomes `gpu_z_gpu_temperature`. Only bound readings are read, so there is no
list to maintain and no cost for the ones you do not use.

A sensor's value is just the number, so it works in calculations. Its unit and GPU-Z's precision are
attributes:

```liquid
{{ vars.gpu_z_gpu_temperature }}                        → 64.5
{{ vars.gpu_z_gpu_temperature.unit }}                   → °C
{{ vars.gpu_z_gpu_temperature.digits }}                 → 1
{{ vars.gpu_z_gpu_temperature | times: 1.8 | plus: 32 }} → 148.1
```

### Unit conversions

Some sensors also get a converted entry, listed right after the original in **Sensors**. These are useful
for bindings, where there is no template to do the maths in:

| GPU-Z unit | Converted entry | Example name |
| --- | --- | --- |
| MB | GB (divided by 1024, as GPU-Z's MB are binary) | `gpu_z_memory_used__dedicated__gb` |
| MB/s | GB/s (divided by 1024) | `gpu_z_<sensor>_gb_s` |
| MHz | GHz | `gpu_z_gpu_clock_ghz` |
| °C | °F | `gpu_z_gpu_temperature_f` |

`gpu_z_is_running` is `true` while GPU-Z is running. While it is `false`, every other GPU-Z variable is
unavailable, and they all come back on their own once GPU-Z starts. Test for that in a template with
`vars.gpu_z_gpu_temperature.state.is_available`.

The available sensors depend on your card and GPU-Z version.

## Coming from Macro Deck 2

- There is no whitelist and no polling frequency to set: bind what you need in the variable browser.
  Readings refresh every second.
- Card-info variables keep their Macro Deck 2 names (`gpu_z_cardname`).
- Each sensor is now **one** variable. Macro Deck 2's `gpu_z_<sensor>_value` is now `gpu_z_<sensor>`,
  `_unit` is `.unit` and `_digits` is `.digits`.
- The **Refresh Variables** action is gone: readings are always current.

## Development

The plugin is a .NET 10 console app built on the Macro Deck 3 plugin SDK.

```
src/DodecaDev.GpuZ/            the plugin
  GpuZ/                        reading and parsing GPU-Z's shared memory, the 1-second monitor
  Variables/                   catalog ids and variable names
  PluginIntegration.cs         the variable provider
tests/DodecaDev.GpuZ.Tests/    PluginTestHarness tests, with GPU-Z faked
```

```bash
dotnet test                                                         # unit and harness tests
macrodeck-plugin run --project src/DodecaDev.GpuZ --stub-host       # run without Macro Deck
macrodeck-plugin test --project src/DodecaDev.GpuZ                  # conformance suite
macrodeck-plugin run --project src/DodecaDev.GpuZ                   # against Macro Deck (Developer Mode on)
```

Install the CLI with `dotnet tool install --global MacroDeck.Plugin.Cli --version 3.0.0-beta.15`.
The SDK version is pinned in `Directory.Packages.props`.

[AGENTS.md](AGENTS.md) holds the plugin-writing rules from the Macro Deck plugin template.

### Releasing

Publish a GitHub release tagged `vX.Y.Z`. [`.github/workflows/release.yml`](.github/workflows/release.yml)
builds, runs the conformance suite and uploads the package to the Macro Deck Creator Portal, which signs
it after review.

## License

[MIT](LICENSE)
