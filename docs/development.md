# Development

## Build

Use PowerShell 7 from the repository root:

```powershell
pwsh .\eng\check-env.ps1
pwsh .\eng\build.ps1 -Configuration Debug
pwsh .\eng\test.ps1 -Configuration Debug
```

## Run the WinUI app

WinUI 3 packaged apps need package identity at runtime. The development run script
builds the app, registers the loose package from the build output, and launches
the app through the shell AUMID:

```powershell
pwsh .\eng\run.ps1 -Configuration Debug -Platform x64
```

If a development package from another build directory is already registered, use:

```powershell
pwsh .\eng\run.ps1 -Configuration Debug -Platform x64 -Reregister
```

## Remove the development package

```powershell
pwsh .\eng\uninstall-dev-package.ps1 -StopProcess
```

This removes only the current user's PulseView development package registration.
