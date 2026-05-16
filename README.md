# GTA World Chat Log Assistant
This program is used to convert the chat logs generated while playing on GTA World into readable text.

![](header.png)

## Getting Started

No installation is required. Simply download the latest [release](https://github.com/blancodagoat/GTAW-Log-Parser/releases) and run the executable.

## Building

The NuGet package dependencies must be restored before compiling the project.

## Contributing

1. Fork Project (<https://github.com/blancodagoat/GTAW-Log-Parser>)
2. Create Branch (`git checkout -b branch_name`)
3. Commit (`git commit -am "Add feature_name"`)
4. Push (`git push origin branch_name`)
5. Create Pull Request

## Roadmap

- Migrate to .NET 8 (LTS) with `Microsoft.NET.Sdk.WindowsDesktop`
- Upgrade MahApps.Metro 1.6 → 2.4 (theming API rewrite required)
- Drop Costura.Fody in favour of `<PublishSingleFile>`
- Extract duplicated controllers (`InitializeServerIp`, `ParseChatLog`, `LocalizationController`) into a shared library

## Building

- The solution now uses SDK-style csproj files. `dotnet build` works for `Parser`.
- `Assistant` has a COM reference (`IWshRuntimeLibrary`) that requires full Visual Studio MSBuild (`MSBuild.exe`) rather than `dotnet build`. Build it from Visual Studio 2022 or the Developer Command Prompt.

## Additional Information

Distributed under the GPLv3 license. See ``LICENSE`` for more information.
