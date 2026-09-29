#!/bin/zsh
# Build and run the headless simulation with the .NET SDK bundled in the Unity
# editor. No separate .NET install is needed.
#
#   tools/simcs/run.sh parity /path/to/ts-trace.json
#   tools/simcs/run.sh seeds 48 ceiling floor
set -eu
here="${0:A:h}"
DOTNET="${DOTNET:-/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
"$DOTNET" build "$here/SimCs.csproj" -c Release -v quiet -nologo -clp:NoSummary 1>&2
"$DOTNET" "$here/bin/Release/net8.0/simcs.dll" "$@"
