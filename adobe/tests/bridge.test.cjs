const {test}=require('node:test');const assert=require('node:assert/strict');
const {adobeTicks,validatePlacements,normalize}=require('../dist/bridge.js');
test('tick conversion is exact at 25 and 50 fps',()=>{assert.equal(adobeTicks(200000),'5080320000');assert.equal(adobeTicks(400000),'10160640000');assert.equal(adobeTicks(864000000000),'21946982400000000');});
test('unsafe or negative times are rejected',()=>{for(const t of [-1,NaN,Infinity,0.2,Number.MAX_SAFE_INTEGER+1])assert.throws(()=>adobeTicks(t));});
test('same-device overlaps rejected before touching Adobe',()=>{assert.throws(()=>validatePlacements([{deviceId:'a',startTicks:0,durationTicks:20},{deviceId:'a',startTicks:10,durationTicks:20}]));});
test('different devices can overlap',()=>{assert.doesNotThrow(()=>validatePlacements([{deviceId:'a',startTicks:0,durationTicks:20},{deviceId:'b',startTicks:0,durationTicks:20}]));});
test('paths compare case-insensitively',()=>assert.equal(normalize('E:\\Day\\Clip.MOV'),normalize('e:/day/clip.mov')));
test('Windows extended drive paths returned by Adobe identify the same file',()=>assert.equal(normalize('\\\\?\\D:\\Montaż\\Projekty\\Dzień 3.prproj'),normalize('D:\\Montaż\\Projekty\\Dzień 3.prproj')));
test('extended UNC paths preserve the server and share identity',()=>{
  assert.equal(normalize('\\\\?\\UNC\\server\\share\\Dzień 3.prproj'),normalize('\\\\server\\share\\Dzień 3.prproj'));
  assert.notEqual(normalize('\\\\?\\UNC\\server\\other\\Dzień 3.prproj'),normalize('\\\\server\\share\\Dzień 3.prproj'));
});
test('device namespaces and other drives are not treated as the target file',()=>{
  const target=normalize('D:\\Projekty\\Dzień 3.prproj');
  for(const other of ['\\\\.\\D:\\Projekty\\Dzień 3.prproj','\\\\?\\GLOBALROOT\\D:\\Projekty\\Dzień 3.prproj','\\\\?\\E:\\Projekty\\Dzień 3.prproj'])assert.notEqual(normalize(other),target);
});
