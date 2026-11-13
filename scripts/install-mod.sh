#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
game_dir="${1:-/home/box/games/HollowKnight-1578}"
python3 - "$game_dir" <<'PY'
import hashlib,pathlib,sys
managed=pathlib.Path(sys.argv[1])/'hollow_knight_Data/Managed'
assembly=managed/'Assembly-CSharp.dll'
if not assembly.exists() or '1.5.78.11833'.encode('utf-16le') not in assembly.read_bytes():
 sys.exit('Expected game version 1.5.78.11833')
if not (managed/'MMHOOK_PlayMaker.dll').exists(): sys.exit('Install API v77 first')
PY
scripts/build.sh "-p:GameDir=$game_dir"
dest="$game_dir/hollow_knight_Data/Managed/Mods/Spellcaster"
mkdir -p "$dest"
for name in Spellcaster.Mod Spellcaster.Core; do
  install -m 644 "src/Spellcaster.Mod/bin/Release/net472/$name.dll" "$dest/$name.dll"
done
sha256sum "$dest/Spellcaster.Mod.dll" "$dest/Spellcaster.Core.dll"
