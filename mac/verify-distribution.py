"""Assess the shipped DMG separately from smoke tests of an unquarantined app."""
from pathlib import Path
import json,os,subprocess,sys,tempfile

ROOT=Path(__file__).resolve().parent.parent
arch=sys.argv[1]
mode=os.environ.get('DIT_MAC_DISTRIBUTION','pilot')
if mode not in ('pilot','notarized'):raise RuntimeError('Unknown distribution mode')
output=ROOT/'artifacts/macos'
dmg=output/f'DIT-0.1.20-mac.1-macOS-{arch}.dmg'

def run(*args,check=True):
    result=subprocess.run(args,capture_output=True,text=True,timeout=1800)
    if check and result.returncode:raise RuntimeError(f'{args[0]} failed: {result.stdout}\n{result.stderr}')
    return result

run('/usr/bin/hdiutil','verify',str(dmg))
if mode=='notarized':
    profile=os.environ.get('APPLE_KEYCHAIN_PROFILE')
    if not profile:raise RuntimeError('Notarized distribution requires APPLE_KEYCHAIN_PROFILE')
    submitted=json.loads(run('/usr/bin/xcrun','notarytool','submit',str(dmg),'--keychain-profile',profile,'--wait','--timeout','20m','--output-format','json').stdout)
    if submitted.get('status')!='Accepted':raise RuntimeError('Apple rejected the DMG: '+json.dumps(submitted))
    run('/usr/bin/xcrun','stapler','staple',str(dmg))
    run('/usr/bin/xcrun','stapler','validate',str(dmg))

with tempfile.TemporaryDirectory(prefix='dit-distribution-') as directory:
    temp=Path(directory).resolve();mount=temp/'mount';mount.mkdir()
    attached=False
    try:
        run('/usr/bin/hdiutil','attach','-readonly','-nobrowse','-mountpoint',str(mount),str(dmg));attached=True
        app=temp/'DIT.app'
        run('/usr/bin/ditto',str(mount/'DIT.app'),str(app))
        run('/usr/bin/codesign','--verify','--deep','--strict',str(app))
        # Simulate a browser download rather than trusting an app in a build folder.
        run('/usr/bin/xattr','-w','com.apple.quarantine','0083;00000000;DIT-distribution-test;',str(app))
        assessment=run('/usr/sbin/spctl','--assess','--verbose=4','--type','execute',str(app),check=False)
        detail=assessment.stdout+assessment.stderr
        accepted=assessment.returncode==0 and 'source=Notarized Developer ID' in detail
        if mode=='notarized':
            run('/usr/bin/xcrun','stapler','validate',str(app))
            if not accepted:raise RuntimeError('Gatekeeper did not accept the notarized downloaded app: '+detail)
        report={'mode':mode,'dmgVerified':True,'copiedAppSignatureVerified':True,
                'quarantineTested':True,'notarizedGatekeeperAccepted':accepted,
                'requiresOpenAnyway':not accepted,'gatekeeperAssessment':detail.strip()}
        (output/f'test-distribution-{arch}.json').write_text(json.dumps(report,indent=2))
        print(json.dumps(report,indent=2))
    finally:
        if attached:run('/usr/bin/hdiutil','detach',str(mount))
