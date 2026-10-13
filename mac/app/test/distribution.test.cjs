const {test}=require('node:test');const assert=require('node:assert/strict');
const {distribution,config}=require('../distribution.cjs');
const {fileOptions}=require('../developer-sign.cjs');
test('pilot explicitly uses ad-hoc signing without claiming notarization',()=>{
  const value=config({});
  assert.equal(distribution({}),'pilot');
  assert.equal(value.mac.identity,null);assert.equal(value.mac.notarize,false);
  assert.equal(value.forceCodeSigning,false);assert.equal(value.mac.sign,undefined);
});
test('notarized distribution refuses missing credentials and development certificates',()=>{
  for(const env of [{DIT_MAC_DISTRIBUTION:'bad'},
    {DIT_MAC_DISTRIBUTION:'notarized'},
    {DIT_MAC_DISTRIBUTION:'notarized',DIT_MAC_SIGN_IDENTITY:'Developer ID Application: Test'},
    {DIT_MAC_DISTRIBUTION:'notarized',DIT_MAC_SIGN_IDENTITY:'Apple Development: Test',APPLE_KEYCHAIN_PROFILE:'DIT'}]){
    assert.throws(()=>config(env));
  }
});
test('production signing requires Developer ID, hardened runtime and notarization',()=>{
  const value=config({DIT_MAC_DISTRIBUTION:'notarized',DIT_MAC_SIGN_IDENTITY:'Developer ID Application: Test (TEAM)',APPLE_KEYCHAIN_PROFILE:'DIT'});
  assert.equal(value.forceCodeSigning,true);assert.equal(value.mac.hardenedRuntime,true);
  assert.equal(value.mac.notarize,true);assert.equal(value.mac.identity,'Test (TEAM)');assert.equal(value.mac.type,'distribution');
  assert.equal(value.mac.sign,'./developer-sign.cjs');
});
test('custom signing fails when the imported Developer ID certificate is missing',async()=>{
  const sign=require('../developer-sign.cjs');
  await assert.rejects(sign({}),/No Developer ID certificate/);
  await assert.rejects(sign({identity:'-'}),/No Developer ID certificate/);
});
test('only .NET hosts receive their required JIT entitlements; Electron keeps its defaults',()=>{
  const defaults={hardenedRuntime:true,entitlements:'electron.plist'};
  for(const host of ['worker/Dit.Mac.Worker','restore/Dit.Mac.Restore']){
    const value=fileOptions('/DIT.app/Contents/Resources/dit/'+host,defaults);
    assert.match(value.entitlements,/dotnet-entitlements.plist$/);assert.equal(value.hardenedRuntime,true);
  }
  assert.equal(fileOptions('/DIT.app/Contents/MacOS/DIT',defaults),defaults);
  assert.equal(fileOptions('/DIT.app/Contents/Resources/dit/worker/ffmpeg',defaults),defaults);
});
