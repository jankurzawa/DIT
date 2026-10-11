const {test}=require('node:test');const assert=require('node:assert/strict');
const {validateApi,validateAudio}=require('../policy.cjs');
test('IPC only addresses the loopback worker API',()=>{
  for(const route of ['/projects','/projects/abc/assets','/session/lock-all','/login'])assert.doesNotThrow(()=>validateApi('POST',route));
  for(const route of ['https://example.com','//example.com','/adobe/next','/projects/../keys','/projects/%2fkeys','/projects/abc?x=1'])assert.throws(()=>validateApi('GET',route));
  assert.throws(()=>validateApi('DELETE','/projects'));
});
test('audio picker excludes video and mixed selections',()=>{
  assert.deepEqual(validateAudio(['/Volumes/Card/Take.WAV','/Volumes/Card/mic.aiff']),['/Volumes/Card/Take.WAV','/Volumes/Card/mic.aiff']);
  for(const files of [['clip.mp4'],['mic.wav','clip.mov'],['audio.wav.exe'],[null]])assert.throws(()=>validateAudio(files));
});
