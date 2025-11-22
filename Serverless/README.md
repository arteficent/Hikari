
$cert = New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation "cert:\LocalMachine\My"
$password = ConvertTo-SecureString -String "yume" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath ".\aspnetcore.pfx" -Password $password


docker build -f "Dockerfile" -t yume-img .

docker run -p 3346:3346 -p 3445:3445 yume-img

