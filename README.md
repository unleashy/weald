# 🌳 Weald

A new programming language (pronounced /wiːld/)

## Development

Weald uses the [dotnet CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/) for development.

To build for debugging, use `dotnet build Weald`.

To publish release binaries, use `dotnet publish -p:PublishProfile=Release -r <rid> Weald`,
replacing `<rid>` with the runtime identifier of the platform you wish to build for, such as
`win-x64` or `linux-x64` as this project uses AOT compilation and single-file executables.

Docs are built from my own crazy YAML format. Use [bun](https://bun.sh): `bun render:watch`.

## Licence

[MIT](LICENSE.txt)
