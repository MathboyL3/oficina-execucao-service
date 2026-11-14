# Cria/atualiza (idempotente) os Secrets do servico no namespace oficina a partir de variaveis de ambiente.
# Nenhum valor fica neste arquivo. Uso: definir as envs abaixo e rodar .\k8s\bootstrap-secrets.ps1
$ErrorActionPreference = 'Stop'
$ns = 'oficina'

function Exigir([string[]]$nomes) {
    $faltando = $nomes | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }
    if ($faltando) { throw "Variaveis de ambiente obrigatorias vazias: $($faltando -join ', ')" }
}
function Env([string]$nome) { [Environment]::GetEnvironmentVariable($nome) }

function Aplicar([string[]]$criar) {
    $yaml = & kubectl @criar --namespace $ns --dry-run=client -o yaml
    if ($LASTEXITCODE -ne 0) { throw "kubectl create falhou: $($criar -join ' ')" }
    $yaml | kubectl apply -f -
    if ($LASTEXITCODE -ne 0) { throw 'kubectl apply falhou' }
}
Exigir @('EXECUCAO_DB_PASSWORD','JWT_SECRET','NEW_RELIC_LICENSE_KEY','GHCR_USER','GHCR_TOKEN')

# A senha do Postgres vive em dois Secrets (POSTGRES_PASSWORD e connection string); ambos derivam de EXECUCAO_DB_PASSWORD.
$senha = Env 'EXECUCAO_DB_PASSWORD'
Aplicar @('create','secret','generic','oficina-execucao-service-secrets',
    "--from-literal=ConnectionStrings__Postgres=Host=execucao-postgres;Port=5432;Database=oficina_execucao;Username=oficina;Password=$senha",
    "--from-literal=Jwt__Secret=$(Env 'JWT_SECRET')",
    "--from-literal=NEW_RELIC_LICENSE_KEY=$(Env 'NEW_RELIC_LICENSE_KEY')")
Aplicar @('create','secret','generic','execucao-postgres-secret',
    "--from-literal=POSTGRES_PASSWORD=$senha")

Aplicar @('create','secret','docker-registry','ghcr-pull',
    '--docker-server=ghcr.io',
    "--docker-username=$(Env 'GHCR_USER')",
    "--docker-password=$(Env 'GHCR_TOKEN')")