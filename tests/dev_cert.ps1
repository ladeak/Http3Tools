# Define the certificate parameters
$certParams = @{
    Subject           = "CN=localhost"
    Type              = "SSLServerAuthentication" # Adds Server EKU (1.3.6.1.5.5.7.3.1)
    CertStoreLocation = "Cert:\LocalMachine\My"    # Installs to the machine's Personal store
    
    # Manually define SANs via OID 2.5.29.17 to ensure IP addresses are formatted correctly
    TextExtension     = @(
        "2.5.29.17={text}DNS=localhost&DNS=*.dev.localhost&DNS=*.dev.internal&DNS=host.docker.internal&DNS=host.containers.internal&IPAddress=127.0.0.1&IPAddress=0000:0000:0000:0000:0000:0000:0000:0001"
    )
    
    # Key & Security Properties
    KeyAlgorithm      = "RSA"
    KeyLength         = 2048
    HashAlgorithm     = "SHA256"
    KeyExportPolicy   = "Exportable" # Set to exportable so you can use it inside Docker/containers
    
    # Modern browsers (Chrome, Safari, Edge) reject certificates valid for more than 398 days.
    # We set this to 397 days to maintain maximum compatibility.
    NotAfter          = (Get-Date).AddDays(397) 
}

# Generate the certificate
$cert = New-SelfSignedCertificate @certParams

# Output details to console
Write-Host "Certificate generated successfully!" -ForegroundColor Green
$cert | Format-List Subject, Thumbprint, NotAfter