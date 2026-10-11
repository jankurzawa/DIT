#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ "$(uname -s)" != Darwin ]]; then echo 'This build must run on macOS.' >&2; exit 1; fi
arch="${1:-$(uname -m)}"
case "$arch" in arm64) rid=osx-arm64;; x64|x86_64) arch=x64; rid=osx-x64;; *) echo 'Unsupported CPU' >&2; exit 1;; esac
mkdir -p artifacts/mac-temp
export TMPDIR="$(pwd)/artifacts/mac-temp/"
python3 mac/prepare-host.py
dotnet test mac/Dit.Mac.Tests -c Release --logger 'trx;LogFileName=mac-adapter-tests.trx'
node --test mac/app/test/*.test.cjs
mkdir -p mac/payload/worker mac/payload/restore mac/payload/adobe mac/payload/docs mac/payload/licenses
dotnet publish mac/Dit.Mac.Worker -c Release -r "$rid" --self-contained true -o mac/payload/worker
dotnet publish mac/Dit.Mac.Restore -c Release -r "$rid" --self-contained true -o mac/payload/restore
find mac/payload -name '*.pdb' -delete
python3 mac/bundle-tools.py
(cd adobe && npm ci && npm run build && npm test)
python3 - <<'PY'
from pathlib import Path
import json,zipfile,shutil,os
root=Path.cwd();adobe=root/'adobe';payload=root/'mac/payload'
manifest=json.loads((adobe/'manifest.json').read_text());manifest['host']=manifest['host'][0]
with zipfile.ZipFile(payload/'adobe/DIT-Premiere.ccx','w',zipfile.ZIP_DEFLATED) as z:
    z.writestr('manifest.json',json.dumps(manifest))
    for name in ('index.html','panel.js','dist/bridge.js'):z.write(adobe/name,name)
for name in ('ADOBE.md','POLACZENIE-PREMIERE.md','INSTALACJA-MACOS.md'):shutil.copy2(root/'docs'/name,payload/'docs'/name)
shutil.copy2(root/'installer/THIRD-PARTY-NOTICES.txt',payload/'licenses/THIRD-PARTY-NOTICES.txt')
dotnet=Path(os.environ.get('DOTNET_ROOT',''))
for name in ('LICENSE.txt','ThirdPartyNotices.txt'):
    if (dotnet/name).exists():shutil.copy2(dotnet/name,payload/'licenses'/('DotNet-'+name))
for file in payload.rglob('*'):
    if file.is_file() and file.suffix.lower() in ('.key','.sqlite','.ditconnection','.prproj','.log'):raise RuntimeError('User data in package: '+str(file))
PY
mkdir -p mac/dit.iconset
for size in 16 32 64 128 256 512; do
  sips -z "$size" "$size" src/Dit.Desktop/Assets/dit.png --out "mac/dit.iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size*2))
  sips -z "$double" "$double" src/Dit.Desktop/Assets/dit.png --out "mac/dit.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns mac/dit.iconset -o mac/dit.icns
(cd mac/app && npm ci && node node_modules/electron/install.js && npm run dist -- --"$arch")
python3 mac/test-package.py "$arch"
cd artifacts/macos
for file in *.dmg *.zip; do shasum -a 256 "$file" > "$file.sha256"; done
