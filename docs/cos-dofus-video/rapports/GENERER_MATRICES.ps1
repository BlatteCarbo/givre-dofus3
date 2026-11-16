# Génère l'inventaire combinatoire, pas des recommandations tactiques.
# PowerShell 5.1+ : .\GENERER_MATRICES.ps1
# Relancer avec -Force uniquement pour régénérer volontairement les CSV.
param([switch]$Force)

$classes = @(
    @{ key='cra'; name='Crâ' }, @{ key='ecaflip'; name='Ecaflip' },
    @{ key='eliotrope'; name='Éliotrope' }, @{ key='eniripsa'; name='Eniripsa' },
    @{ key='enutrof'; name='Enutrof' }, @{ key='feca'; name='Féca' },
    @{ key='forgelance'; name='Forgelance' }, @{ key='huppermage'; name='Huppermage' },
    @{ key='iop'; name='Iop' }, @{ key='osamodas'; name='Osamodas' },
    @{ key='ouginak'; name='Ouginak' }, @{ key='pandawa'; name='Pandawa' },
    @{ key='roublard'; name='Roublard' }, @{ key='sacrieur'; name='Sacrieur' },
    @{ key='sadida'; name='Sadida' }, @{ key='sram'; name='Sram' },
    @{ key='steamer'; name='Steamer' }, @{ key='xelor'; name='Xélor' },
    @{ key='zobal'; name='Zobal' }
)
$pairSources = @{
    'cra|enutrof' = 'VID-DUO19-01@04:50'
    'eniripsa|sadida' = 'VID-DUO19-01@08:39'
    'feca|sacrieur' = 'VID-DUO19-01@07:07'
    'feca|sram' = 'VID-DUO19-01@10:59'
    'iop|pandawa' = 'VID-DUO19-01@01:13'
    'enutrof|iop' = 'VID-DUO19-01@01:13'
    'iop|sram' = 'VID-DUO19-01@01:13'
    'osamodas|ouginak' = 'VID-DUO19-01@12:46'
    'forgelance|pandawa' = 'VID-DUO19-01@14:35'
    'osamodas|zobal' = 'VID-DUO19-01@16:57'
    'enutrof|xelor' = 'VID-DUO19-01@18:15'
    'cra|ecaflip' = 'VID-DUO19-01@20:13'
    'sadida|steamer' = 'VID-DUO19-01@21:25'
    'feca|pandawa' = 'VID-DUO19-01@23:11'
    'huppermage|sadida' = 'VID-HUPPER-DUO-01@03:15'
    'huppermage|steamer' = 'VID-HUPPER-DUO-01@01:05'
    'huppermage|zobal' = 'VID-HUPPER-DUO-01@05:15'
    'eliotrope|sadida' = 'VID-DUO19-01@30:18'
    'pandawa|roublard' = 'VID-DUO19-01@28:18'
    'roublard|sram' = 'VID-DUO19-01@33:35'
    'sram|xelor' = 'VID-DUO19-01@10:59'
}
$trioHyp = @(
    'cra|enutrof|pandawa',
    'eniripsa|iop|pandawa',
    'feca|huppermage|steamer',
    'enutrof|roublard|sram'
)
$quadSources = @{
    'cra|enutrof|feca|pandawa' = 'VID-TEAM4-01@04:25'
    'cra|eniripsa|feca|pandawa' = 'VID-TEAM4-01@05:05'
    'enutrof|feca|pandawa|sadida' = 'VID-TEAM4-01@05:36'
    'feca|huppermage|sadida|sram' = 'VID-TEAM4-01@06:08'
}

function New-Row([int[]]$indices, [int]$size, [int]$seq) {
    $p=@($indices | ForEach-Object { $classes[$_] })
    $key = ($p | ForEach-Object { $_.key }) -join '|'
    $names = ($p | ForEach-Object { $_.name }) -join ' + '
    $status='indexee_non_etudiee'
    $source=''
    $priority=''
    if ($size -eq 2 -and $pairSources.ContainsKey($key)) {
        $status='declare_video_non_valide'
        $source=$pairSources[$key]
        $priority='a_reconstituer'
    }
    if ($size -eq 3 -and $trioHyp -contains $key) {
        $status='hypothese_a_tester'
        $source='SYNERGIES_DUO_TRIO_QUATUOR.md'
        $priority='haute'
    }
    if ($size -eq 4 -and $quadSources.ContainsKey($key)) {
        $status='declare_video_non_valide'
        $source=$quadSources[$key]
        $priority='a_reconstituer'
    }
    [PSCustomObject]@{
        id = ('{0}-{1:D4}' -f @('','SOLO','DUO','TRIO','QUAT')[$size], $seq)
        taille = $size
        classes = $names
        statut = $status
        provenance = $source
        priorite = $priority
        prochaine_preuve = 'sorts et variante par niveau; initiative; etat initial; video horodatee; test positif et negatif'
    }
}

$dest=Split-Path -Parent $MyInvocation.MyCommand.Path
$collections=@{}
$duos=New-Object System.Collections.ArrayList
$trios=New-Object System.Collections.ArrayList
$quads=New-Object System.Collections.ArrayList
$n=$classes.Count
for($i=0; $i -lt $n; $i++) {
 for($j=$i+1; $j -lt $n; $j++) {
   [void]$duos.Add((New-Row @($i,$j) 2 ($duos.Count+1)))
   for($k=$j+1; $k -lt $n; $k++) {
     [void]$trios.Add((New-Row @($i,$j,$k) 3 ($trios.Count+1)))
     for($l=$k+1; $l -lt $n; $l++) {
       [void]$quads.Add((New-Row @($i,$j,$k,$l) 4 ($quads.Count+1)))
     }
   }
 }
}
$collections['MATRICE_DUOS_171.csv']=$duos
$collections['MATRICE_TRIOS_969.csv']=$trios
$collections['MATRICE_QUATUORS_3876.csv']=$quads
foreach($name in $collections.Keys) {
  $path=Join-Path $dest $name
  if((Test-Path -LiteralPath $path) -and -not $Force) {
    Write-Output ('SKIP existant : '+$name)
  } else {
    $collections[$name] | Export-Csv -LiteralPath $path -Delimiter ';' -Encoding UTF8 -NoTypeInformation
    Write-Output ('CREATED '+$name+' '+$collections[$name].Count+' rows')
  }
}

