// Ad-hoc signing permits local execution on Apple Silicon. It is deliberately
// not described as Apple Developer ID signing or notarization.
const {execFileSync}=require('node:child_process');
const fs=require('node:fs');const path=require('node:path');
module.exports=async context=>{
  if(context.electronPlatformName!=='darwin')throw Error('macOS packaging requires a Mac runner.');
  const bundle=path.join(context.appOutDir,'DIT.app');
  function walk(root){for(const entry of fs.readdirSync(root,{withFileTypes:true})){
    const file=path.join(root,entry.name);if(entry.isSymbolicLink())continue;
    if(entry.isDirectory()){walk(file);continue;}
    const kind=execFileSync('/usr/bin/file',['-b',file],{encoding:'utf8'});
    if(kind.includes('Mach-O'))execFileSync('/usr/bin/codesign',['--force','--sign','-',file],{stdio:'pipe'});
  }}
  walk(path.join(bundle,'Contents','Resources','dit'));
  execFileSync('/usr/bin/codesign',['--force','--deep','--sign','-',bundle],{stdio:'pipe'});
  execFileSync('/usr/bin/codesign',['--verify','--deep','--strict',bundle],{stdio:'pipe'});
};
