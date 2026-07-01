# Bustrap Copilot Instructions

## Build, test, and lint commands

This repository is a .NET/WPF solution centered on `Bustrap.sln` and `Bustrap/Bustrap.csproj`.

```powershell
# first-time clone (README requirement)
git submodule update --init --recursive

# restore dependencies
dotnet restore .\Bustrap.sln

# build main solution
dotnet build .\Bustrap.sln -c Debug
dotnet build .\Bustrap.sln -c Release

# run the launcher from source
dotnet run --project .\Bustrap\Bustrap.csproj
```

Test/lint status in this repo:

- There are currently no test projects (`*Test*.csproj`) in this repository, so `dotnet test` has no runnable targets.
- There is no repository-level lint command configured for the main project.
- If tests are added later, run one test with:

```powershell
dotnet test <test-project>.csproj --filter "FullyQualifiedName~<TestName>"
```

## High-level architecture

- `App.xaml.cs` is the orchestration entrypoint. Startup flow is:
  1) initialize locale/logging/http headers  
  2) resolve install location (registry + portable fallback)  
  3) install or repair via `Installer` when needed  
  4) initialize `Paths` and load persisted JSON state (`Settings`, `State`, `RobloxState`, `FastFlags`, `DownloadStats`)  
  5) dispatch to `LaunchHandler`.
- `LaunchSettings` parses command-line flags and Roblox URI arguments; `LaunchHandler` decides whether to open installer, settings, watcher, Roblox player/studio bootstrap flow, or uninstall flow.
- `Bootstrapper` is the runtime update/launch engine. It performs connectivity checks (`RobloxInterfaces/Deployment.cs`), version/package resolution, download/extract, and launch logic, while updating UI via `IBootstrapperDialog`.
- Roblox product-specific behavior is abstracted behind `AppData/IAppData` implementations (`RobloxPlayerData`, `RobloxStudioData`) and consumed by `Bootstrapper`.
- `wpfui/` is a git submodule and `Bustrap/Bustrap.csproj` references `wpfui/src/Wpf.Ui/Wpf.Ui.csproj` directly; UI-related changes may require touching both repos.

## Key repository conventions

- **Global app singletons live on `App`**: settings/state/services are accessed as `App.Settings`, `App.State`, `App.FastFlags`, `App.Logger`, etc. New cross-cutting services follow this pattern.
- **Settings persistence uses `JsonManager<T>`**: file-backed state classes live in `Models/Persistable`, are loaded at startup, and saved explicitly. The manager uses write-to-temp then replace semantics; preserve that behavior when changing persistence.
- **Deferred settings side effects use `BaseTask` + `App.PendingSettingTasks`**: viewmodels stage changes first, then `Settings/MainWindowViewModel.SaveSettings()` executes queued tasks after writing JSON.
- **Logging format is consistent**: methods usually define `const string LOG_IDENT = "Type::Method"` and log through `App.Logger.WriteLine/WriteException`.
- **Localization path**:
  - UI text should come from `Resources/Strings.resx` (`Strings.*` accessors), not hardcoded strings.
  - Supported languages are centrally defined in `Locale.SupportedLocales`; update both locale map and resource entries when adding UI language support.
- **Launch/install behavior is Windows-registry driven**: installer/uninstaller and protocol registration are tightly coupled to `Installer`, `WindowsRegistry`, and path helpers in `Paths`; keep these flows consistent when adding launch flags or install behaviors.
