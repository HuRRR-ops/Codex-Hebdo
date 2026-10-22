# Validation - Codex Hebdo

## Correctifs de fiabilité — 21 octobre 2026

Les suites PowerShell sont exécutées par `Build.ps1` sous Windows PowerShell 5.1.
Elles peuvent aussi être exécutées sous PowerShell 7 :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WindhawkPreparation.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/SymbolCache.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/InstallTransaction.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/InstallEntry.Tests.ps1
```

- 12 contrôles de préparation Windhawk : lecture réelle des deux INI UTF-16 par
  l'API Win32 (BOM, Portable et AppDataPath), avec distribution factice, puis
  réparation de fichiers existants en UTF-8 BOM en conservant leurs valeurs.
- 18 contrôles du cache de symboles : structure MSF, GUID, âges Info/DBI,
  troncature, indices invalides, corruption détectée par SHA-256, cache valide,
  téléchargement invalide et reprise après indisponibilité du réseau.
- 12 contrôles transactionnels : prévalidation, panne après écriture, fichier
  verrouillé, restauration des fichiers/configurations, valeurs et types du
  registre, nettoyage, redémarrage précédent et succès complet.
- 6 contrôles du véritable point d'entrée Install.ps1 : DLL manquante avant
  arrêt, échec de lancement, restauration du compagnon et du mod, puis installation
  réussie sans remplacement des préférences. Processus et réseau sont simulés.

Ces tests utilisent des dossiers temporaires et une clé de registre de test
distincte. Ils n'installent pas de mod dans Explorer. Les 141 assertions C#,
8 tests des helpers de l'installateur et 5 contrôles de rechargement restent en place.

Le validateur a également été vérifié en lecture seule sur deux PDB Taskbar
Microsoft présents sur la machine. Un téléchargement réel depuis Microsoft,
la réparation du fichier volontairement corrompu et la réutilisation du cache
ont été exercés dans un dossier isolé, sans toucher aux symboles installés.
Le format suit la documentation
[MSF LLVM](https://llvm.org/docs/PDB/MsfFile.html) ; les règles d'âge suivent
[PDB1::OpenValidate4 de Microsoft](https://github.com/microsoft/microsoft-pdb/blob/master/PDB/dbi/pdb.cpp).
Un PDB public peut avoir un âge Info supérieur à l'âge de l'image, tandis que
l'âge DBI doit correspondre (sauf l'ancien cas DBI zéro).

Le retour arrière couvre les erreurs détectées dans la transaction, y compris
les fichiers de licence, l'autostart et l'inscription de désinstallation du Setup.
Une coupure de courant ou une terminaison forcée de PowerShell n'est pas une
transaction récupérée automatiquement. Si la restauration échoue elle-même,
les sauvegardes et leur manifeste sont conservés et leur chemin est signalé.

## Release 1.2.3

141 tests applicatifs, 8 tests d'installation et 5 vérifications de récupération des symboles. Publication autonome Windows x64 et vérification de l'intégrité du contenu embarqué. Le script de récupération est inclus dans la sortie publiée du compagnon. La récupération a été exercée sur les symboles de la version Windows installée ; aucun nouveau redémarrage Windows n'a été effectué pour cette validation.

Les résultats ci-dessous décrivent les validations historiques de la release 1.2.3.

## Couverture

- Récupération après mise à jour Windows : `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/TaskbarRecovery.Tests.ps1` vérifie la suppression ciblée du cache en erreur, la conservation des autres entrées, la restauration après rechargement, l'absence de rechargement inutile et le respect des modules désactivés.

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


## Installateur 1.2.3

- 141 tests applicatifs et 8 tests dédiés au téléchargement, au cache, à l’intégrité, aux codes de sortie et aux délais des processus.
- Construction autonome Windows x64 et vérification SHA-256 de tous les fichiers du paquet intégré.
- Préparation réelle sans dépendances dans un dossier isolé, avec téléchargement et extraction de Windhawk 1.7.3 et Codex CLI 0.162.1.
- Lecture réussie du quota réel avec le Codex téléchargé.
- Installation par le fichier Setup.exe sur la machine de développement : code de sortie 0, préférences inchangées, autostart conservé, runtime local présent et widget attaché.
- Une machine Windows vierge, le parcours navigateur d’un nouveau compte et les configurations Windhawk standard ne sont pas couverts par ces essais. Windhawk standard n’est pas migré automatiquement.

### Isolation Windhawk (prévention des conflits avec les jeux)

`Tests/WindhawkIsolation.Tests.ps1` : 9 contrôles couvrent provenance, migration,
encodage UTF-16, ciblage absolu d'Explorer, idempotence et préservation des réglages
personnalisés et des autres mods (y compris désactivés).
`Tests/InstallEntry.Tests.ps1` : 13 contrôles, dont retrait des réglages au rollback,
installation effective du fichier et demande de redémarrage du moteur actif.
Ces tests utilisent des dossiers temporaires et des processus simulés. Ils ne
prouvent ni la compatibilité d'un jeu particulier ni le rendu du widget en session.

Le renforcement de l'isolation couvre aussi le rechargement d'un réglage déjà
présent, le refus avant modification d'une autre instance active ou d'autres mods,
et la conservation des réglages lors d'une dérogation explicite `-AllowSharedWindhawk`.
