# Validation - Codex Hebdo 1.1.1

102 tests automatisés réussis sous Windows 11 x64, .NET 10 et Windhawk 1.7.3.

## Couverture

- Sélection du quota hebdomadaire (10 080 minutes), rejet des réponses ambiguës,
  calcul du restant et expiration des données après 75 secondes.
- Compteur de réinitialisations : valeurs inconnues, zéro, fraîcheur indépendante
  et notifications partielles.
- JSON-RPC : initialisation, réponses, notifications, erreurs et déconnexion avec
  un processus serveur factice ; aucun quota de test n’est envoyé au widget installé.
- Préférences : normalisation, persistance, aperçu sans enregistrement et restauration.
- Logos : SVG local, proportions, transparence, teinte et refus des références externes.
- Interface FR/EN : ouverture sans mutation des tailles, saisie numérique incomplète
  sans saut à la borne maximale, saisie valide immédiatement appliquée.
- Fenêtre non redimensionnable, adaptation aux onglets et boutons contenus dans leurs
  groupes. Rendus français/anglais inspectés à 125 %.
- Transport du double-clic : la commande U renvoie les mêmes statistiques et réglages
  que V, avant et après l’ouverture des options.

- Réinitialisation : conservation des contrôles, des dimensions, de l’onglet et de
  la langue ; remise des valeurs par défaut et mise à jour de l’aperçu.

## Vérifications d’intégration

Compilation C# et C++ réussie ; installation et rechargement du module dans Explorer,
IPC réel, lecture des quotas, arrêt du processus enfant, désinstallation/réinstallation
et restauration de la disposition native exercés.

Les erreurs transitoires de transport conservent la dernière mesure sans renouveler
son horodatage. Une donnée explicitement indisponible ou expirée reste indisponible.

## Reproduire

```powershell
dotnet run --project Tests -c Release
.\Build.ps1 -WindhawkPath "$env:LOCALAPPDATA\Programs\Windhawk"
.\scripts\Test-Ipc.ps1 -Command V
```

L’option `--render-options` des tests produit les rendus dans le dossier temporaire.

## Limites

Les tests de protocole et les rendus hors écran ne simulent pas le double-clic physique
sur Explorer. Les changements de moniteur/DPI en cours d’utilisation, les configurations
très chargées et un redémarrage complet d’Explorer restent à valider. Le widget dépend
de structures privées Windows et vise la barre horizontale principale de Windows 11 x64.
