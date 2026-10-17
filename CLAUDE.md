# CoffeeCup

Windows tray app (C#, .NET Framework 4.8, WinForms) plus an Inno Setup installer.
CI builds on `windows-latest`; this repo can only be compile-checked on Linux
(`dotnet build src/CoffeeCup/CoffeeCup.csproj -c Release`), never run.

## Releases

Every build the user receives must be published as a GitHub Release:

- Bump `<Version>` in `src/CoffeeCup/CoffeeCup.csproj` in every change that alters the app or installer.
- Pushing to `main` makes `.github/workflows/build.yml` create release `v<Version>` with the installer.
  If that release already exists, nothing new is published, so don't forget the bump.
- Don't push tags yourself; the workflow creates them.
- Changes to `.github/workflows/` can't be pushed from Claude sessions (no `workflow` scope);
  hand the file to the user to commit through the GitHub web UI.
