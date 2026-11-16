[CmdletBinding()]
param([switch]$Preview)
$ErrorActionPreference = 'Stop'
$steps = @(
    'Préparation humaine : personnage TEST, objet banal, lot x1. Couper watcher, achat auto et relist. Ouvrir Mettre en vente > Diagnostic UID avant toute opération. Aucun autre opérateur sur ce PID.',
    'Vente humaine : relever UID sac et quantité, puis confirmer volontairement dans le panneau (deux clics séparés de 700 ms à 6 s). Ne pas répéter. Vérifier sac ET annonce dans le client officiel ; candidat ou inject=OK ne sont pas des ACK.',
    'Reprix humain : seulement après vente vérifiée. Relever ancien UID lot, modifier ce seul lot, observer nouveau candidat puis vérifier GID/quantité/prix dans le client officiel. Le moteur ne corrèle pas strictement le reprix.',
    'Retrait humain : actualiser et sélectionner UID lot ACTUEL, retirer une seule fois puis vérifier annonce et retour au sac. NeedsReview : arrêter, rapprocher manuellement ; ne pas renvoyer automatiquement.'
)
Write-Output 'Guide local uniquement : aucun appel au jeu, au réseau, au relais ou aux comptes.'
foreach ($step in $steps) { Write-Output $step }
if ($Preview) { return }
foreach ($index in 0..($steps.Count - 1)) {
    Write-Output ("Etape {0}: {1}" -f ($index + 1), $steps[$index])
    $answer = Read-Host 'Après contrôle humain : OK / INCERTAIN / REFUSE / STOP'
    if ($answer -cne 'OK') {
        Write-Output 'Arrêt du guide. Aucune confirmation réseau déduite, aucune opération répétée.'
        return
    }
}
Write-Output 'Contrôles humains déclarés terminés. Ce guide ne certifie aucun ACK réseau.'
Write-Output 'Pour les traces UID uniquement : tools/R6-Hdv-Operator-Test.ps1 -PidFilter <PID> ; relire le CSV avant partage.'
