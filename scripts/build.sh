#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET_BIN="${DOTNET_BIN:-$HOME/.dotnet/dotnet}"
"$DOTNET_BIN" build Spellcaster.sln -c Release --nologo "$@"
"$DOTNET_BIN" test tests/Spellcaster.Core.Tests -c Release --no-build --nologo
