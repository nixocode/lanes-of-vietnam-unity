#!/bin/zsh
# The EditMode suite without Unity (PLAN §12.73a): build, then run every [Test] and [TestCase] of SimTests.cs.
#
#   tools/editrun/run.sh                       all of it
#   tools/editrun/run.sh With_the_mind         the tests whose name contains this
#
# Run from anywhere; the tests that read the project's files are run from the project folder.
set -eu
here="${0:A:h}"
proj="${here:h:h}"
DOTNET="${DOTNET:-/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
"$DOTNET" build "$here/EditRun.csproj" -c Release -v quiet -nologo -clp:NoSummary 1>&2
cd "$proj"
"$DOTNET" "$here/bin/Release/net8.0/editrun.dll" "$@"
