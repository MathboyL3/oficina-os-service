<#
.SYNOPSIS
  Teste ponta a ponta local dos 3 servicos (os, billing, execucao) via docker compose.
.DESCRIPTION
  Pre-requisito: os 3 composes no ar (os-service primeiro) e a rede externa 'oficina-dev'.
  Cenarios: 1 fluxo feliz, 2 compensacao por falta de peca, 3 timeout (Quartz), 4 falha de estorno + acao manual.
  Os cenarios 3 e 4 recriam os-api / billing-api com um override de env (arquivo temporario) e restauram o padrao no fim.
  Sai com codigo 1 em qualquer falha, informando o passo que travou.
#>
param(
    [string]$OsUrl = 'http://localhost:5001',
    [string]$BillingUrl = 'http://localhost:5002',
    [string]$ExecucaoUrl = 'http://localhost:5003',
    [int]$TimeoutSegundos = 90,
    [string]$Cenarios = '1,2,3,4'
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dirOs = Join-Path $raiz 'oficina-os-service'
$dirBilling = Join-Path $raiz 'oficina-billing-service'
$script:Token = $null

function Passo($msg) { Write-Host ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $msg) }
function Falhar($msg) { Write-Host "FALHOU: $msg" -ForegroundColor Red; exit 1 }

function Api([string]$Metodo, [string]$Url, $Corpo = $null, [bool]$Auth = $true) {
    $h = @{}
    if ($Auth -and $script:Token) { $h['Authorization'] = "Bearer $($script:Token)" }
    $p = @{ Method = $Metodo; Uri = $Url; Headers = $h; ContentType = 'application/json' }
    if ($null -ne $Corpo) { $p['Body'] = ($Corpo | ConvertTo-Json -Depth 10) }
    try { return Invoke-RestMethod @p }
    catch {
        $detalhe = ''
        if ($_.Exception.Response) {
            try { $detalhe = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch { }
            $detalhe = "HTTP $([int]$_.Exception.Response.StatusCode) $detalhe"
        } else { $detalhe = $_.Exception.Message }
        throw "$Metodo $Url -> $detalhe"
    }
}

function Esperar([string]$Descricao, [scriptblock]$Condicao, [int]$Segundos = $TimeoutSegundos) {
    Passo "aguardando: $Descricao"
    $fim = (Get-Date).AddSeconds($Segundos)
    $ultimo = $null
    while ((Get-Date) -lt $fim) {
        try { $r = & $Condicao; if ($r) { return $r } } catch { $ultimo = $_.Exception.Message }
        Start-Sleep -Milliseconds 1000
    }
    Falhar "tempo esgotado ($Segundos s) no passo: $Descricao. Ultimo erro: $ultimo"
}

function Executar([string]$Descricao, [scriptblock]$Bloco) {
    try { & $Bloco } catch { Falhar "$Descricao :: $($_.Exception.Message)" }
}

function NovoCpf {
    $d = 1..9 | ForEach-Object { Get-Random -Minimum 0 -Maximum 10 }
    if (($d | Select-Object -Unique).Count -eq 1) { $d[0] = ($d[0] + 1) % 10 }
    foreach ($n in 10, 11) {
        $s = 0
        for ($i = 0; $i -lt $n - 1; $i++) { $s += $d[$i] * ($n - $i) }
        $r = ($s * 10) % 11; if ($r -eq 10) { $r = 0 }
        $d += $r
    }
    return ($d -join '')
}

function NovaPlaca {
    $l = 'ABCDEFGHJKLMNPRSTUVWXYZ'
    $c = { $l[(Get-Random -Maximum $l.Length)] }
    return "{0}{1}{2}{3}{4}{5}{6}" -f (& $c), (& $c), (& $c), (Get-Random -Maximum 10), (& $c), (Get-Random -Maximum 10), (Get-Random -Maximum 10)
}

function AguardarSaude {
    foreach ($u in $OsUrl, $BillingUrl, $ExecucaoUrl) {
        Esperar "health/ready de $u" { (Invoke-WebRequest "$u/health/ready" -UseBasicParsing).StatusCode -eq 200 } 120 | Out-Null
    }
}

# Recria um servico do compose com um override de env (arquivo temporario). $Env = $null restaura o padrao.
function RecriarServico([string]$Dir, [string]$Servico, [hashtable]$Env) {
    $dargs = @('compose', '-f', 'docker-compose.yml')
    $tmp = $null
    if ($Env) {
        $tmp = Join-Path $env:TEMP ("e2e-override-{0}.yml" -f [guid]::NewGuid().ToString('N'))
        $linhas = @('services:', "  ${Servico}:", '    environment:')
        foreach ($k in $Env.Keys) { $linhas += "      ${k}: `"$($Env[$k])`"" }
        [IO.File]::WriteAllLines($tmp, $linhas, (New-Object Text.UTF8Encoding($false)))
        $dargs += @('-f', $tmp)
    }
    $dargs += @('up', '-d', '--no-build', '--force-recreate', '--no-deps', $Servico)
    Push-Location $Dir
    try {
        $ErrorActionPreference = 'Continue'; $saida = (& docker @dargs 2>&1 | Out-String); $ErrorActionPreference = 'Stop'
        if ($LASTEXITCODE -ne 0) { Falhar "docker compose up $Servico falhou: $saida" }
    } finally { Pop-Location; if ($tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue } }
    Start-Sleep -Seconds 3
    AguardarSaude
}

function Autenticar {
    $cred = @{ Nome = 'Gerente E2E'; Email = 'gerente.e2e@oficina.local'; Senha = 'Senha@E2E-12345'; Role = 'Gerente' }
    Passo 'bootstrap do usuario Gerente (409 = ja existe)'
    try { Api POST "$OsUrl/api/auth/bootstrap" $cred $false | Out-Null }
    catch { if ($_ -notmatch 'HTTP 409') { Falhar "bootstrap :: $_" } else { Passo 'bootstrap ja realizado; usando login' } }
    Passo 'login'
    $t = Api POST "$OsUrl/api/auth/login" @{ Email = $cred.Email; Senha = $cred.Senha } $false
    $script:Token = $t.accessToken
    if (-not $script:Token) { Falhar 'login sem accessToken' }
}

# Cadastra catalogo e abre OS com itens. $QtdPeca = quantidade pedida da peca; estoque da peca = 10.
function PrepararOs([int]$QtdPeca) {
    $sfx = [guid]::NewGuid().ToString('N').Substring(0, 8)
    Passo 'cadastrando servico e peca no execucao'
    $srv = Api POST "$ExecucaoUrl/api/servicos" @{ Nome = "Revisao $sfx"; Descricao = 'Revisao geral'; ValorBase = 150.0; TempoEstimadoMinutos = 60 }
    $peca = Api POST "$ExecucaoUrl/api/pecas" @{ Nome = "Filtro $sfx"; Codigo = "F-$sfx"; Unidade = 'un'; Valor = 40.0; QuantidadeInicial = 10; LimiteMinimo = 1 }
    Passo 'cadastrando cliente, veiculo e abrindo OS'
    $cli = Api POST "$OsUrl/api/clientes" @{ Nome = "Cliente $sfx"; Documento = (NovoCpf); Email = "c$sfx@teste.local"; Telefone = '11999990000' }
    $vei = Api POST "$OsUrl/api/veiculos" @{ ClienteId = $cli.id; Placa = (NovaPlaca); Marca = 'Fiat'; Modelo = 'Uno'; Ano = 2020 }
    $os = Api POST "$OsUrl/api/ordens-servico" @{ ClienteId = $cli.id; VeiculoId = $vei.id }
    Passo "OS $($os.id) aberta; definindo itens (peca x$QtdPeca)"
    Api POST "$OsUrl/api/ordens-servico/$($os.id)/itens" @{
        Servicos = @(@{ Id = $srv.id; Quantidade = 1 })
        Pecas = @(@{ Id = $peca.id; Quantidade = $QtdPeca })
    } | Out-Null
    return @{ OsId = $os.id; PecaId = $peca.id }
}

function EnviarEAguardarOrcamento([string]$OsId) {
    Passo 'enviando orcamento'
    Api POST "$OsUrl/api/ordens-servico/$OsId/enviar-orcamento" | Out-Null
    $orc = Esperar "orcamento da OS $OsId no billing" {
        $l = @(Api GET "$BillingUrl/api/orcamentos?osId=$OsId"); if ($l.Count -gt 0) { $l[0] } else { $null }
    }
    return $orc
}

function EsperarOs([string]$OsId, [string]$Descricao, [scriptblock]$Teste) {
    return Esperar "OS $OsId : $Descricao" {
        $o = Api GET "$OsUrl/api/ordens-servico/$OsId"
        if (& $Teste $o) { $o } else { $null }
    }
}

function TextoHistorico($os) {
    $t = @($os.historico | ForEach-Object { $_.observacao })
    if ($os.saga) { $t += $os.saga.motivoCancelamento }
    return ($t -join ' | ')
}

function Cenario1 {
    Passo '=== Cenario 1: fluxo feliz ==='
    $p = PrepararOs 2
    $orc = EnviarEAguardarOrcamento $p.OsId
    Passo "aprovando orcamento $($orc.id)"
    Api POST "$BillingUrl/api/orcamentos/$($orc.id)/aprovar" | Out-Null
    EsperarOs $p.OsId 'saga.estado = EmExecucao' { param($o) $o.saga.estado -eq 'EmExecucao' } | Out-Null
    $ex = Esperar "execucao da OS $($p.OsId)" {
        @(Api GET "$ExecucaoUrl/api/execucoes?take=100") | Where-Object { $_.ordemServicoId -eq $p.OsId } | Select-Object -First 1
    }
    Passo "execucao $($ex.id): iniciar, diagnostico, finalizar"
    Api POST "$ExecucaoUrl/api/execucoes/$($ex.id)/iniciar" | Out-Null
    Api POST "$ExecucaoUrl/api/execucoes/$($ex.id)/diagnostico" @{ Observacoes = 'Diagnostico e2e' } | Out-Null
    Api POST "$ExecucaoUrl/api/execucoes/$($ex.id)/finalizar" | Out-Null
    EsperarOs $p.OsId 'status = Finalizada' { param($o) $o.status -eq 'Finalizada' } | Out-Null
    Passo 'entregando'
    $e = Api POST "$OsUrl/api/ordens-servico/$($p.OsId)/entregar"
    if ($e.status -ne 'Entregue') { Falhar "esperado Entregue, veio $($e.status)" }
    Write-Host 'OK fluxo feliz' -ForegroundColor Green
}

function Cenario2 {
    Passo '=== Cenario 2: compensacao por falta de peca ==='
    $p = PrepararOs 999
    $orc = EnviarEAguardarOrcamento $p.OsId
    Api POST "$BillingUrl/api/orcamentos/$($orc.id)/aprovar" | Out-Null
    $os = EsperarOs $p.OsId 'status = Cancelada' { param($o) $o.status -eq 'Cancelada' }
    if ((TextoHistorico $os) -notmatch 'Falha na reserva de pe') { Falhar "historico sem 'Falha na reserva de pecas': $(TextoHistorico $os)" }
    Passo 'conferindo estoque intacto no execucao'
    $peca = Api GET "$ExecucaoUrl/api/pecas/$($p.PecaId)"
    if ($peca.quantidadeDisponivel -ne 10 -or $peca.quantidadeReservada -ne 0) {
        Falhar "estoque alterado: disponivel=$($peca.quantidadeDisponivel) reservada=$($peca.quantidadeReservada)"
    }
    Esperar "orcamento $($orc.id) Cancelado no billing" {
        (Api GET "$BillingUrl/api/orcamentos/$($orc.id)").status -eq 'Cancelado'
    } | Out-Null
    Write-Host 'OK compensacao' -ForegroundColor Green
}

function Cenario3 {
    Passo '=== Cenario 3: timeout de aprovacao via Quartz ==='
    Passo 'recriando os-api com Saga__Timeouts__AguardandoAprovacao=00:00:30'
    RecriarServico $dirOs 'os-api' @{ 'Saga__Timeouts__AguardandoAprovacao' = '00:00:30' }
    try {
        $p = PrepararOs 1
        EnviarEAguardarOrcamento $p.OsId | Out-Null
        Passo 'NAO aprovando; aguardando o Quartz disparar o timeout'
        $os = EsperarOs $p.OsId 'status = Cancelada por timeout' { param($o) $o.status -eq 'Cancelada' } 120
        if ((TextoHistorico $os) -notmatch 'expirou') { Falhar "historico sem motivo de timeout: $(TextoHistorico $os)" }
    } finally {
        Passo 'restaurando os-api com timeouts padrao'
        RecriarServico $dirOs 'os-api' $null
        Autenticar
    }
    Write-Host 'OK timeout' -ForegroundColor Green
}

function Cenario4 {
    Passo '=== Cenario 4: FalharEstorno + acao manual ==='
    Passo 'recriando billing-api com MercadoPago__FakeComportamento=FalharEstorno'
    RecriarServico $dirBilling 'billing-api' @{ 'MercadoPago__FakeComportamento' = 'FalharEstorno' }
    $id = $null
    try {
        $p = PrepararOs 999
        $id = $p.OsId
        $orc = EnviarEAguardarOrcamento $id
        Api POST "$BillingUrl/api/orcamentos/$($orc.id)/aprovar" | Out-Null
        EsperarOs $id 'saga.estado = AguardandoAcaoManual' { param($o) $o.saga.estado -eq 'AguardandoAcaoManual' } | Out-Null
        $pend = @(Api GET "$OsUrl/api/admin/sagas") | Where-Object { $_.ordemServicoId -eq $id }
        if (-not $pend) { Falhar 'OS nao aparece em GET /api/admin/sagas' }
    } finally {
        Passo 'restaurando billing-api com FakeComportamento=Aprovar'
        RecriarServico $dirBilling 'billing-api' @{ 'MercadoPago__FakeComportamento' = 'Aprovar' }
    }
    Passo 'reprocessando a compensacao'
    Api POST "$OsUrl/api/admin/ordens-servico/$id/reprocessar-compensacao" | Out-Null
    EsperarOs $id 'status = Cancelada' { param($o) $o.status -eq 'Cancelada' } | Out-Null
    Write-Host 'OK acao manual' -ForegroundColor Green
}

Executar 'saude dos servicos' { AguardarSaude }
Executar 'autenticacao' { Autenticar }
foreach ($c in ($Cenarios -split '[,; ]+' | Where-Object { $_ } | ForEach-Object { [int]$_ })) {
    switch ($c) {
        1 { Cenario1 }
        2 { Cenario2 }
        3 { Cenario3 }
        4 { Cenario4 }
    }
}
Write-Host 'E2E concluido sem falhas.' -ForegroundColor Green
exit 0
