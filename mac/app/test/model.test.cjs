const {test}=require('node:test');const assert=require('node:assert/strict');
const {filterAssets,zonedIso,escape,processing}=require('../ui/model.js');
test('workflow filters and numeric sorting preserve processing vs sync distinction',()=>{
  const assets=[{id:'a',batchId:'a',logicalName:'Clip 10',ownerName:'Jan',bytes:100,isMedia:true},{id:'b',batchId:'b',logicalName:'Clip 2',ownerName:'Ala',bytes:20,isMedia:true}];
  const workflows=[{assetId:'a',ingested:true,backedUp:true,proxyRequired:true,proxy:false,inPremiere:true},{assetId:'b',ingested:true,backedUp:true,proxyRequired:false,inPremiere:true,synchronized:false}];
  assert.equal(filterAssets(assets,workflows,[],'','processing')[0].asset.id,'a');
  assert.equal(filterAssets(assets,workflows,[],'','ready')[0].asset.id,'b');
  assert.equal(filterAssets(assets,workflows,[],'','synced').length,0);
  assert.equal(filterAssets(assets,workflows,[],'ala')[0].asset.id,'b');
  assert.equal(filterAssets(assets,workflows,[],'','all','size')[0].asset.id,'a');
  assert.equal(filterAssets(assets,workflows,[{id:'a',error:'disk'}],'','error')[0].asset.id,'a');
  assert.equal(processing(workflows[1]),false);
});
test('calendar converts the chosen timezone and rejects a DST gap',()=>{
  assert.equal(zonedIso('2026-10-11','14:02','Europe/Warsaw'),'2026-10-11T12:02:00.000Z');
  assert.equal(zonedIso('2026-12-11','14:02','Europe/Warsaw'),'2026-12-11T13:02:00.000Z');
  assert.throws(()=>zonedIso('2026-03-29','02:30','Europe/Warsaw'));
});
test('filenames and source names cannot inject markup',()=>{assert.equal(escape('<img src=x onerror="x">'),'&lt;img src=x onerror=&quot;x&quot;&gt;');});
