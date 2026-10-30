param(
    [Parameter(Mandatory = $true)][string]$RegistryName
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

docker info --format '{{.ServerVersion}}' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Docker Engine indisponivel.' }

$commit = git -C $root rev-parse --short HEAD
if ($LASTEXITCODE -ne 0 -or -not $commit) { throw 'Nao foi possivel obter o commit Git.' }

$registry = az acr show --name $RegistryName --query loginServer -o tsv
if ($LASTEXITCODE -ne 0 -or -not $registry) { throw 'ACR nao localizado na assinatura atual.' }

$image = "$registry/trezzecloud-catalog-api:fase4-$commit"

az acr login --name $RegistryName
if ($LASTEXITCODE -ne 0) { throw 'Falha ao autenticar no ACR.' }

docker build --platform linux/amd64 -f (Join-Path $root 'Dockerfile') -t $image $root
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a imagem Docker.' }

docker push $image
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar a imagem Docker.' }

Write-Host "Imagem publicada: $image"
az acr repository show-tags --name $RegistryName --repository trezzecloud-catalog-api -o table
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel consultar as tags do ACR.' }
