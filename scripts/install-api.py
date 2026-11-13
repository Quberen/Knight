#!/usr/bin/env python3
"""Install only the pinned, checksum-verified Linux API, preserving originals."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import sys
import zipfile

GAME_VERSION = "1.5.78.11833"
TAG = "1.5.78.11833-77"
SHA256 = "5d2327d76b2c48e2694e43e90689d867eaa9ac1d9e6fd2998c93087de7b61ddb"
URL = f"https://github.com/hk-modding/api/releases/download/{TAG}/moddingapi.v77.linux.zip"
root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "/home/box/games/HollowKnight-1578").resolve()
managed = root / "hollow_knight_Data/Managed"
assembly = managed / "Assembly-CSharp.dll"
if not assembly.is_file() or GAME_VERSION.encode("utf-16le") not in assembly.read_bytes():
    sys.exit("Refusing installation: expected Hollow Knight 1.5.78.11833 Assembly-CSharp.dll")
cache = pathlib.Path(__file__).resolve().parent.parent / ".cache/api"
cache.mkdir(parents=True, exist_ok=True)
archive = cache / "moddingapi.v77.linux.zip"
if not archive.exists():
    subprocess.run(["curl", "--proto", "=https", "--proto-redir", "=https", "-fL", URL, "-o", str(archive)], check=True)
if hashlib.sha256(archive.read_bytes()).hexdigest() != SHA256:
    sys.exit("API archive SHA-256 mismatch; nothing installed")
backup = root / f"modding-api-backup-vanilla-{GAME_VERSION}"
backup.mkdir(exist_ok=True)
manifest_path = backup / "SHA256.json"
manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else {}
with zipfile.ZipFile(archive) as bundle:
    # Validate the full archive and every existing backup before touching installed files.
    files = {name: bundle.read(name) for name in bundle.namelist() if name != "README.md"}
    for name, expected in manifest.items():
        if hashlib.sha256((backup / name).read_bytes()).hexdigest() != expected:
            sys.exit(f"Original backup checksum mismatch: {name}")
    for name, data in files.items():
        if pathlib.Path(name).name != name:
            sys.exit("Unexpected archive layout")
        dest = managed / name
        if dest.exists() and dest.read_bytes() != data and not (backup / name).exists():
            shutil.copy2(dest, backup / name)
            manifest[name] = hashlib.sha256(dest.read_bytes()).hexdigest()
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    for name, data in files.items():
        dest = managed / name
        temporary = dest.with_suffix(dest.suffix + ".installing")
        temporary.write_bytes(data)
        temporary.replace(dest)
print(f"Installed API {TAG}; archive SHA-256 verified: {SHA256}")
print(f"Original backup: {backup}")
