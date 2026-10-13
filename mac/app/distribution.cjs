// Signing is explicit: a pilot build must never be reported as notarized.
function distribution(env=process.env){
  const mode=env.DIT_MAC_DISTRIBUTION||'pilot';
  if(!['pilot','notarized'].includes(mode))throw Error('Unknown DIT_MAC_DISTRIBUTION. Use pilot or notarized.');
  if(mode==='notarized'){
    for(const key of ['DIT_MAC_SIGN_IDENTITY','APPLE_KEYCHAIN_PROFILE']){
      if(!env[key]?.trim())throw Error(`Notarized distribution requires ${key}; refusing an ad-hoc fallback.`);
    }
    if(!env.DIT_MAC_SIGN_IDENTITY.startsWith('Developer ID Application: ')||!env.DIT_MAC_SIGN_IDENTITY.slice(26).trim())throw Error('A Developer ID Application signing identity is required.');
  }
  return mode;
}
function config(env=process.env){
  const base=require('./package.json').build;
  const notarized=distribution(env)==='notarized';
  return {...base,forceCodeSigning:notarized,mac:{...base.mac,
    // electron-builder chooses the certificate type itself and rejects its
    // prefix in the qualifier, even though codesign uses the full name.
    identity:notarized?env.DIT_MAC_SIGN_IDENTITY.slice('Developer ID Application: '.length).trim():null,
    hardenedRuntime:notarized,notarize:notarized,
    ...(notarized?{type:'distribution',sign:'./developer-sign.cjs'}:{})}};
}
module.exports={distribution,config};
