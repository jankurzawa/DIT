. "$PSScriptRoot/env.ps1"
Push-Location $ditRepo
try {
    & $ditDotnet test tests/Dit.Tests -m:1 --logger 'trx;LogFileName=dit-tests.trx'
    if($LASTEXITCODE-ne 0){throw 'Testy .NET nie powiodły się.'}
    node .tools/typescript/package/bin/tsc -p adobe/tsconfig.json
    if($LASTEXITCODE-ne 0){throw 'Kompilacja TypeScript nie powiodła się.'}
    node --test adobe/tests/*.test.cjs
    if($LASTEXITCODE-ne 0){throw 'Testy panelu Adobe nie powiodły się.'}
} finally {Pop-Location}
