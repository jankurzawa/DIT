const path=require('node:path');
function fileOptions(file,defaults={}){
  const name=file.replaceAll('\\','/');
  if(name.endsWith('/dit/worker/Dit.Mac.Worker')||name.endsWith('/dit/restore/Dit.Mac.Restore')){
    return {...defaults,entitlements:path.join(__dirname,'dotnet-entitlements.plist')};
  }
  return defaults;
}
async function sign(options){
  if(!options.identity||options.identity==='-')throw Error('No Developer ID certificate was found; refusing an ad-hoc fallback.');
  const {signAsync}=require('@electron/osx-sign');
  await signAsync({...options,optionsForFile:file=>fileOptions(file,options.optionsForFile?.(file)||{})});
}
module.exports=sign;
module.exports.fileOptions=fileOptions;
