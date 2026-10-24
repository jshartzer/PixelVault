# Trading-card foil rendering

October 23, 2026: the two long-standing foil pixel-test failures are resolved.

The embedded shader was compiled as `ps_3_0`. WPF does not run PS 3.0 shaders in software, including `RenderTargetBitmap`, so the tests received the unmodified artwork. This also affected software-rendered application surfaces. Microsoft documents the limitation in [ShaderEffect remarks](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.effects.shadereffect).

The compile script now targets `ps_2_0`, and the checked-in `.ps` resource was rebuilt. The shader reuses its existing moving broad reflection falloff and removes a redundant small ridge brightening term to fit the model's arithmetic limit. Moving diffraction/silver reflection, honeycomb/grain, sparkles, rounded transparency and premultiplied alpha remain. The same bytecode supports hardware and WPF software rendering; no runtime compiler is needed.

The original reflection, texture and alpha assertions all pass unchanged. A fourth regression check verifies the bundled bytecode uses the software-compatible model. Rendered left/right light positions were inspected. `foil-fixed.trx` records the initial three passes; the complete four-case check is included in `tracking-portability-focused.trx` and the full green `foil-portability-full.trx` run (1,063 passes, no failures/skips).

Recompile after HLSL edits using `scripts/Compile-TradingCardFoil.ps1`; retain the model compatibility and rendering checks. The review build is `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`, published version 0.077.000 unchanged. Older documentation's shader-failure counts are historical results.
