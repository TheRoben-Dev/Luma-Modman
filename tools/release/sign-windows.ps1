# Code-signs the Windows build (Luma.exe, luma-cli.exe) so SmartScreen and antivirus see who made it.
#
#   pwsh tools/release/sign-windows.ps1 -Dir build/godot-windows            # signs if a signing secret is set, else does nothing
#   pwsh tools/release/sign-windows.ps1 -Dir <copy> -SelfSigned             # CI check with a throwaway certificate
#
# Signing secrets (GitHub → Settings → Secrets and variables → Actions), one of:
#   Azure Trusted Signing: AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET,
#                          AZURE_SIGNING_ENDPOINT (e.g. https://weu.codesigning.azure.net),
#                          AZURE_SIGNING_ACCOUNT, AZURE_SIGNING_PROFILE
#   A .pfx certificate:    WINDOWS_CERT_PFX (the .pfx file, base64), WINDOWS_CERT_PASSWORD
# Writes "signed=true|false" to $env:GITHUB_OUTPUT when it's set.
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [switch]$SelfSigned
)
$ErrorActionPreference = 'Stop'

$files = @('Luma.exe', 'luma-cli.exe') | ForEach-Object { Join-Path $Dir $_ }
foreach ($f in $files) {
    if (-not (Test-Path $f)) { throw "Missing $f" }
}

function Out-Result([bool]$signed) {
    if ($env:GITHUB_OUTPUT) { "signed=$($signed.ToString().ToLower())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
}

# The newest x64 signtool from the Windows SDK (10.0.22621+ is needed for Trusted Signing's dlib).
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object { [version]($_.Directory.Parent.Name) } | Select-Object -Last 1
if (-not $signtool) { throw 'signtool.exe not found (Windows SDK)' }
$signtool = $signtool.FullName
Write-Host "signtool: $signtool"

$common = @('sign', '/fd', 'SHA256', '/d', 'Luma', '/du', 'https://github.com/TheRoben-Dev/Luma-Modman')
$work = Join-Path ([IO.Path]::GetTempPath()) ("luma-sign-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    if ($SelfSigned) {
        # A throwaway certificate: proves the build still starts after signing. Not trusted by anyone.
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Luma CI test' -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddDays(1)
        $pfx = Join-Path $work 'test.pfx'
        $pw = ConvertTo-SecureString -String 'luma-ci' -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pw | Out-Null
        & $signtool @common /f $pfx /p 'luma-ci' @files
        if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
        Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
    }
    elseif ($env:AZURE_SIGNING_ENDPOINT -and $env:AZURE_SIGNING_ACCOUNT -and $env:AZURE_SIGNING_PROFILE -and $env:AZURE_CLIENT_ID) {
        # Azure Trusted Signing: the key stays in Azure; signtool calls it through Microsoft's dlib.
        # Its credentials come from AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET.
        $nuget = Join-Path $work 'nuget.exe'
        Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget
        & $nuget install Microsoft.Trusted.Signing.Client -OutputDirectory $work -NonInteractive | Out-Host
        $dlib = Get-ChildItem $work -Recurse -Filter 'Azure.CodeSigning.Dlib.dll' | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
        if (-not $dlib) { throw 'Azure.CodeSigning.Dlib.dll not found in Microsoft.Trusted.Signing.Client' }
        $meta = Join-Path $work 'metadata.json'
        @{ Endpoint = $env:AZURE_SIGNING_ENDPOINT; CodeSigningAccountName = $env:AZURE_SIGNING_ACCOUNT; CertificateProfileName = $env:AZURE_SIGNING_PROFILE } |
            ConvertTo-Json | Out-File -FilePath $meta -Encoding utf8
        & $signtool @common /tr 'http://timestamp.acs.microsoft.com' /td SHA256 /dlib $dlib.FullName /dmdf $meta @files
        if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
    }
    elseif ($env:WINDOWS_CERT_PFX) {
        $pfx = Join-Path $work 'cert.pfx'
        [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:WINDOWS_CERT_PFX))
        & $signtool @common /f $pfx /p $env:WINDOWS_CERT_PASSWORD /tr 'http://timestamp.digicert.com' /td SHA256 @files
        if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
    }
    else {
        Write-Host '::notice::No code-signing secret is set: the Windows build is released unsigned (SmartScreen will warn).'
        Out-Result $false
        return
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

foreach ($f in $files) {
    $sig = Get-AuthenticodeSignature $f
    if (-not $sig.SignerCertificate) { throw "$f has no signature after signing" }
    Write-Host "$([IO.Path]::GetFileName($f)): signed by $($sig.SignerCertificate.Subject) ($($sig.Status))"
}
Out-Result $true
