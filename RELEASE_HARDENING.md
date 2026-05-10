# Release Hardening

Atlas cannot truly encrypt managed .NET code once it is shipped. The practical approach is to reduce inspection value and ship hardened release builds.

## Current defaults

- Release builds use optimization and symbol-free output.
- Debug symbols are disabled for Release.
- `publish-atlas.ps1` removes any `.pdb` files from the publish output.
- `publish-atlas.ps1` publishes self-contained multi-file output by default because single-file publishing is currently producing an invalid zero-byte executable in this environment.

## Recommended publish flow

Run a normal hardened release publish:

```powershell
.\publish-atlas.ps1
```

If you specifically want to try single-file packaging again:

```powershell
.\publish-atlas.ps1 -SingleFile
```

Publish without launching the app:

```powershell
.\publish-atlas.ps1 -SkipLaunch
```

Run an external protection tool after publish:

```powershell
.\publish-atlas.ps1 -ObfuscatorCommand "C:\Tools\Obfuscator\protect.bat" -ObfuscatorArguments "{publishDir}"
```

You can also use environment variables:

```powershell
$env:ATLAS_OBFUSCATOR_CMD = "C:\Tools\Obfuscator\protect.bat"
$env:ATLAS_OBFUSCATOR_ARGS = "{publishDir}"
.\publish-atlas.ps1
```

`{publishDir}` is replaced with the actual Atlas publish directory before the tool is run.

## Recommendation

Use the current self-contained multi-file Release publish as the baseline and only add an obfuscator that you have tested against WPF, WebView2, and reflection-heavy code paths. Do not add a random obfuscator to the main build without validating startup, bindings, serialization, and commands.