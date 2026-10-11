"""Copy native Homebrew command line tools with their complete non-system dylib closure.

Rewrites loader references so the installed app has no dependency on Homebrew.
The resulting binaries are ad-hoc signed by the packaging hook afterwards.
"""
from pathlib import Path
import subprocess, shutil, re, json

ROOT=Path(__file__).resolve().parent.parent
DEST=ROOT/'mac/payload/worker'
LIB=DEST/'lib'
LIC=ROOT/'mac/payload/licenses/tools'
LIB.mkdir(parents=True,exist_ok=True)
LIC.mkdir(parents=True,exist_ok=True)
seen={}

def run(*args):
    return subprocess.check_output(args,text=True).strip()

def deps(source):
    return [line.strip().split(' (compatibility')[0] for line in run('/usr/bin/otool','-L',str(source)).splitlines()[1:]]

def resolve(dependency,source):
    value=dependency.replace('@loader_path',str(source.parent)).replace('@executable_path',str(source.parent))
    if value.startswith('@rpath/'):
        rpaths=re.findall(r'cmd LC_RPATH\s+cmdsize \d+\s+path (.*?) \(offset',run('/usr/bin/otool','-l',str(source)))
        for rpath in rpaths:
            candidate=Path(rpath.replace('@loader_path',str(source.parent)).replace('@executable_path',str(source.parent)))/value[len('@rpath/'):]
            if candidate.exists():return candidate.resolve()
        raise RuntimeError(f'Unresolved dependency: {source}: {dependency}')
    result=Path(value)
    if not result.exists():raise RuntimeError(f'Missing dependency: {source}: {dependency}')
    return result.resolve()

def copy(source,target):
    source=source.resolve()
    if target in seen:
        if seen[target]!=source:raise RuntimeError(f'Dylib filename collision: {target}')
        return
    seen[target]=source
    shutil.copy2(source,target);target.chmod(0o755)
    for dep in deps(source):
        if dep.startswith(('/usr/lib/','/System/Library/')):continue
        origin=resolve(dep,source)
        if origin==source:continue  # dylib's own install ID
        child=LIB/origin.name
        copy(origin,child)
        replacement=('@loader_path/' if target.parent==LIB else '@loader_path/lib/')+child.name
        subprocess.run(['/usr/bin/install_name_tool','-change',dep,replacement,str(target)],check=True)
    if target.parent==LIB:subprocess.run(['/usr/bin/install_name_tool','-id','@loader_path/'+target.name,str(target)],check=True)

for name in ('ffmpeg','ffprobe','7zz'):
    origin=Path(shutil.which(name) or '')
    if not origin.is_file():raise RuntimeError(f'{name} must be installed by Homebrew on the build runner')
    copy(origin,DEST/name)

# Keep build provenance and upstream license files beside the bundled tools.
formulae=json.loads(run('brew','info','--json=v2','--installed'))['formulae']
(LIC/'homebrew-build-provenance.json').write_text(json.dumps(formulae,indent=2))
cellar=Path(run('brew','--cellar'))
for formula in formulae:
    folder=cellar/formula['name']
    for version in folder.glob('*'):
        candidates=list(version.glob('*'))+list((version/'share/doc').glob('**/*'))
        for item in candidates:
            if item.is_file() and re.search(r'license|copying|copyright',item.name,re.I):
                target=LIC/formula['name']/item.name
                target.parent.mkdir(exist_ok=True)
                shutil.copy2(item,target)
for file,source in seen.items():
    for dep in deps(file):
        if dep.startswith(('/usr/local/','/opt/homebrew/')):raise RuntimeError(f'External dependency remains: {file}: {dep}')
print(f'Bundled {len(seen)} native tool binaries and dylibs')
