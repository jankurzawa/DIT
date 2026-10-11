"""Run the actual packaged app, worker and native encoders on the Mac build host."""
from pathlib import Path
import os,subprocess,tempfile,json,urllib.request,time,stat,signal
ROOT=Path(__file__).resolve().parent.parent
arch=__import__('sys').argv[1]
output=ROOT/'artifacts/macos'
bundle=output/('mac-arm64' if arch=='arm64' else 'mac')/'DIT.app'
worker=bundle/'Contents/Resources/dit/worker'
with tempfile.TemporaryDirectory(prefix='dit-mac-test-') as directory:
    # /var is a symlink on macOS; the copy engine deliberately rejects links.
    temp=Path(directory).resolve();data=temp/'Data';report=temp/'smoke.json'
    env=os.environ.copy();env.update(DIT_TEST_DATA=str(data),DIT_TEST_PORT='47839',DIT_SMOKE=str(report),DOTNET_ROOT=str(temp/'no-dotnet'),PATH='/usr/bin:/bin:/usr/sbin:/sbin')
    result=subprocess.run([str(bundle/'Contents/MacOS/DIT')],env=env,capture_output=True,text=True,timeout=120)
    if result.returncode:raise RuntimeError(f'Packaged app failed: {result.stdout}\n{result.stderr}\n{report.read_text() if report.exists() else "No renderer report"}')
    checks=json.loads(report.read_text());assert 'error' not in checks,checks
    (output/f'preview-macos-{arch}.png').write_bytes(Path(str(report)+'.png').read_bytes())
    assert stat.S_IMODE((data/'local.key').stat().st_mode)==0o600
    assert stat.S_IMODE((data/'adobe.key').stat().st_mode)==0o600
    checks['passed'].append('Connection keys use owner-only filesystem permissions')
    sample=temp/'sample.mp4'
    subprocess.run([str(worker/'ffmpeg'),'-v','error','-f','lavfi','-i','color=c=blue:s=128x72:r=25','-t','1','-c:v','libx264','-pix_fmt','yuv420p',str(sample)],env=env,check=True,timeout=30)
    probe=json.loads(subprocess.check_output([str(worker/'ffprobe'),'-v','error','-show_streams','-of','json',str(sample)],env=env,text=True));assert probe['streams'][0]['width']==128
    subprocess.run([str(worker/'7zz'),'a','-t7z',str(temp/'sample.7z'),str(sample)],env=env,check=True,capture_output=True,timeout=30)
    subprocess.run([str(worker/'7zz'),'t',str(temp/'sample.7z')],env=env,check=True,capture_output=True,timeout=30)
    checks['passed'].append('Bundled native FFmpeg/ffprobe encode H.264 and 7zz verifies archives without Homebrew in PATH')
    # Exercise real diskutil on the runner's physical APFS disk through /admin.
    process=subprocess.Popen([str(worker/'Dit.Mac.Worker'),'--data',str(data),'--listen','http://127.0.0.1:47839','--adobe-auto-launch','false'],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    try:
        key=(data/'local.key').read_text().strip()
        def api(route,body=None,token=None):
            headers={'X-DIT-Key':key,'Content-Type':'application/json'}
            if token:headers['X-DIT-Session']=token
            request=urllib.request.Request('http://127.0.0.1:47839'+route,data=None if body is None else json.dumps(body).encode(),headers=headers)
            with urllib.request.urlopen(request,timeout=30) as response:return json.loads(response.read() or b'null')
        for attempt in range(50):
            try:api('/health');break
            except OSError:time.sleep(.2)
        config={'id':'smoke','name':'Mac Smoke','mainRoots':[str(temp/'Main')],'backupRoots':[str(temp/'Backup')],'proxyRoot':str(temp/'Proxy'),'premiereRoot':str(temp/'Premiere'),'tabs':[{'id':'admin','name':'Admin','kind':'Admin','userId':'admin'},{'id':'audio','name':'Audio','kind':'Audio','userId':'audio'}],'devices':[{'id':'mic','name':'Mikroport 1','audio':True}],'audioSources':[{'deviceId':'mic','pattern':'','channels':1,'channelNames':['Mikroport 1']}]}
        api('/projects',{'config':config,'users':[{'id':'admin','name':'Admin','password':'','admin':True},{'id':'audio','name':'Audio','password':'','admin':False}]})
        token=api('/login',{'projectId':'smoke','tabId':'admin','userId':'admin','password':''})['token']
        admin=api('/projects/smoke/admin',token=token)
        assert all(v['info'] and v['info']['stableIdentity'] for v in admin['volumes']),admin['volumes']
        assert len({v['info']['physicalDiskId'] for v in admin['volumes']})==1
        checks['passed'].append('Real diskutil resolves APFS volumes; folders on the same physical disk share identity')
    finally:
        process.terminate();process.wait(timeout=20)
    (output/f'test-macos-{arch}.json').write_text(json.dumps(checks,indent=2))
    print(json.dumps(checks,indent=2))
