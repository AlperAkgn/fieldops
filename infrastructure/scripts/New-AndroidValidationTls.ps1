#Requires -Version 7.0
using namespace System.Security.Cryptography
using namespace System.Security.Cryptography.X509Certificates

param(
    [Parameter(Mandatory)]
    [string] $LanAddress
)

$ErrorActionPreference = 'Stop'
$ip = [IPAddress]::Parse($LanAddress)
if ($ip.ToString() -ne $LanAddress -or $LanAddress -notmatch '^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)') {
    throw 'Choose the Windows Wi-Fi or Ethernet private IPv4 address on the phone network.'
}
if (-not (Get-NetIPAddress -AddressFamily IPv4 -IPAddress $LanAddress -ErrorAction SilentlyContinue)) {
    throw 'The selected IPv4 address is not assigned to this PC.'
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$directory = Join-Path $repo '.certs/android-validation'
$envFile = Join-Path $repo '.env.android-validation'
if ((Test-Path -LiteralPath $directory) -or (Test-Path -LiteralPath $envFile)) {
    throw 'Android validation TLS files already exist. Preserve them while the validation APK or database is in use.'
}

$rootKey = [RSA]::Create(3072)
$serverKey = [RSA]::Create(2048)
$rootCert = $null
$serverPublicCert = $null
$serverCert = $null
try {
    $from = [DateTimeOffset]::UtcNow.AddMinutes(-5)
    $rootRequest = [CertificateRequest]::new('CN=FieldOps Android Validation CA', $rootKey, [HashAlgorithmName]::SHA256, [RSASignaturePadding]::Pkcs1)
    [void] $rootRequest.CertificateExtensions.Add([X509BasicConstraintsExtension]::new($true, $true, 0, $true))
    [void] $rootRequest.CertificateExtensions.Add([X509KeyUsageExtension]::new([X509KeyUsageFlags]::KeyCertSign, $true))
    [void] $rootRequest.CertificateExtensions.Add([X509SubjectKeyIdentifierExtension]::new($rootRequest.PublicKey, $false))
    $rootCert = $rootRequest.CreateSelfSigned($from, $from.AddDays(14))

    $serverRequest = [CertificateRequest]::new("CN=$LanAddress", $serverKey, [HashAlgorithmName]::SHA256, [RSASignaturePadding]::Pkcs1)
    [void] $serverRequest.CertificateExtensions.Add([X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $keyUsage = [X509KeyUsageFlags]::DigitalSignature -bor [X509KeyUsageFlags]::KeyEncipherment
    [void] $serverRequest.CertificateExtensions.Add([X509KeyUsageExtension]::new($keyUsage, $true))
    $serverAuth = [OidCollection]::new()
    [void] $serverAuth.Add([Oid]::new('1.3.6.1.5.5.7.3.1'))
    [void] $serverRequest.CertificateExtensions.Add([X509EnhancedKeyUsageExtension]::new($serverAuth, $false))
    $names = [SubjectAlternativeNameBuilder]::new()
    $names.AddIpAddress($ip)
    $names.AddDnsName('localhost')
    [void] $serverRequest.CertificateExtensions.Add($names.Build())
    $serverPublicCert = $serverRequest.Create($rootCert, $from, $from.AddDays(7), [RandomNumberGenerator]::GetBytes(16))
    $serverCert = [RSACertificateExtensions]::CopyWithPrivateKey($serverPublicCert, $serverKey)

    $password = [Convert]::ToHexString([RandomNumberGenerator]::GetBytes(24))
    $pfx = $serverCert.Export([X509ContentType]::Pfx, $password)
    $ca = $rootCert.Export([X509ContentType]::Cert)
    New-Item -ItemType Directory -Path $directory | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $directory 'server.pfx'), $pfx)
    [IO.File]::WriteAllBytes((Join-Path $directory 'ca.cer'), $ca)
    [IO.File]::WriteAllLines($envFile, @(
        "FIELDOPS_ANDROID_LAN_IP=$LanAddress"
        'FIELDOPS_HTTPS_CERT_PATH=./.certs/android-validation/server.pfx'
        "FIELDOPS_HTTPS_CERT_PASSWORD=$password"
        'POSTGRES_DB=fieldops_android_validation'
        'POSTGRES_PORT=5551'
        "POSTGRES_PASSWORD=$([Convert]::ToHexString([RandomNumberGenerator]::GetBytes(24)))"
        "JWT_SIGNING_KEY=$([Convert]::ToHexString([RandomNumberGenerator]::GetBytes(32)))"
        'MAILPIT_SMTP_PORT=1041'
        'MAILPIT_WEB_PORT=8041'
        "MINIO_ROOT_PASSWORD=$([Convert]::ToHexString([RandomNumberGenerator]::GetBytes(24)))"
    ))
    Write-Output "Phone API origin: https://${LanAddress}:5451"
    Write-Output 'Public CA: .certs/android-validation/ca.cer'
    Write-Output 'Local server configuration: .env.android-validation'
} finally {
    if ($serverCert) { $serverCert.Dispose() }
    if ($serverPublicCert) { $serverPublicCert.Dispose() }
    if ($rootCert) { $rootCert.Dispose() }
    $serverKey.Dispose()
    $rootKey.Dispose()
}
