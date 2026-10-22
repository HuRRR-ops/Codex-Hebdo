# Guide technique

La release 1.2.3 fournit un installateur automatique Windows x64. Il inclut .NET, télécharge les dépendances manquantes depuis leurs releases officielles et prépare Windhawk portable. Une connexion au compte reste interactive si nécessaire.

Les instructions ci-dessous concernent la compilation et l’installation manuelles. Compiler avec `Build.ps1` avant de lancer `Install.ps1`.

### Durcissement de l'installation et du cache

La préparation manuelle écrit les INI Windhawk en UTF-16, y compris sous Windows
PowerShell 5.1. Les symboles présents sont vérifiés avant réutilisation : structure
MSF, identité GUID et âges Info/DBI compatibles avec l'image Windows. Une empreinte
SHA-256 locale détecte les modifications ultérieures. Un téléchargement de
remplacement doit passer la validation avant de remplacer atomiquement le fichier.
Cette empreinte locale n'est pas une signature de l'éditeur ; les anciens caches
sans empreinte sont d'abord validés structurellement avant création de l'empreinte.

`Install.ps1` prépare et sauvegarde tous les fichiers avant d'arrêter le compagnon.
En cas d'erreur détectée, il restaure les fichiers, la configuration du mod et les
valeurs du registre modifiées, puis redémarre le compagnon s'il fonctionnait avant
la tentative. Les préférences sont exclues des fichiers remplacés. Le Setup utilise
la même transaction pour ses licences et son inscription de désinstallation.
Les dépendances téléchargées et le cache de symboles sont conservés.

Le retour arrière ne protège pas automatiquement d'une coupure de courant ou
d'une terminaison forcée. S'il ne peut pas restaurer un fichier ou une valeur,
il conserve les sauvegardes et `rollback.json` dans le dossier temporaire indiqué
par l'erreur. Les tests d'échec sont décrits dans [TESTS.md](../TESTS.md).

## Installateur automatique

- Source : `Installer/`, construction : `scripts/Build-Installer.ps1` après `Build.ps1`.
- Construction avec .NET 10 et 7-Zip installé sur la machine de compilation.
- Le build récupère 7-Zip 26.04 et ses sources avec vérification SHA-256. Les binaires, licences et sources correspondantes sont inclus dans le paquet.
- Le runtime .NET est publié en mode autonome. La machine cible n’a besoin ni du SDK, ni d’un runtime installé séparément.
- Les dépendances téléchargées sont épinglées : Windhawk 1.7.3 et Codex CLI 0.162.1. SHA-256 obligatoire ; signature Authenticode également vérifiée pour Windhawk.
- Windhawk portable compatible et Codex existants sont réutilisés. Windhawk standard ou une version portable incompatible est préservé et signalé, sans écrasement.
- Codex installé par l’assistant reste dans `%LOCALAPPDATA%\Programs\CodexHebdo-Codex`. Aucun changement global de PATH.
- Cache de téléchargement : `%LOCALAPPDATA%\CodexHebdo-Setup\Downloads`. Journal : `%LOCALAPPDATA%\CodexHebdo-Setup\1.2.3\setup.log`. La sortie des commandes de connexion Codex n’est pas enregistrée.
- Une annulation est possible avant l’application des fichiers du widget. Pendant cette phase finale, l’annulation est désactivée.
- L’application est ajoutée à la liste des applications installées de Windows. La désinstallation conserve Codex et Windhawk.
- L’exécutable de l’installateur n’est pas signé par un certificat de signature de code.

### Vérifications de la release 1.2.3

141 tests du compagnon et 8 tests de l’installateur. Préparation réelle de Windhawk et Codex dans un dossier isolé sans dépendances, lecture du quota via le Codex téléchargé, vérification du paquet intégré et mise à jour locale avec conservation des préférences et de l’autostart.

Ces essais ne constituent pas un test sur une machine Windows vierge. Le parcours de connexion d’un nouveau compte et la diversité des configurations Windows restent à valider plus largement.

## Installation manuelle

Installation par utilisateur dans `%LOCALAPPDATA%\CodexHebdo`, avec Windhawk portable dans `%LOCALAPPDATA%\Programs\Windhawk`. Démarrage automatique des deux via les valeurs `CodexHebdo` et `CodexHebdo.Windhawk` de HKCU\Software\Microsoft\Windows\CurrentVersion\Run. Aucun droit administrateur utilisé.

Après compilation, pour installer depuis ce dossier :

```powershell
.\Install.ps1
```

Dépendances : Windows 11 x64, runtime .NET Desktop 10 x64, Codex déjà connecté, Windhawk 1.7.3 ou compatible. Sur une autre machine, installer Windhawk depuis [windhawk.net](https://windhawk.net/). Si 7-Zip est présent, `scripts\Prepare-Windhawk.ps1` peut préparer la version portable officielle avec vérification de signature. Cette dépendance comprend son éditeur et son compilateur et occupe environ 0,84 Go ; le compteur lui-même est beaucoup plus petit.

Pour une installation **standard** de Windhawk, le script installe le compagnon puis indique l’étape manuelle : Windhawk → **Créer un nouveau mod** → remplacer le code par `dist\codex-hebdo-taskbar.wh.cpp` → **Compiler (F7)** et activer. Pour une installation portable, le script configure aussi le mod automatiquement.

Les symboles de `taskbar.dll` sont préparés par PowerShell avant activation. Le mod désactive explicitement les serveurs de symboles et caches en ligne dans Explorer. Le compagnon vérifie également les symboles au lancement puis toutes les cinq minutes, dans un processus séparé. Après une mise à jour Windows, il récupère le PDB correspondant depuis Microsoft, efface uniquement l'erreur de cache associée et recharge le module Codex Hebdo si celui-ci est activé. Une connexion indisponible déclenche une nouvelle tentative au passage suivant ; les autres modules restent intacts. Cette récupération automatique concerne les installations portables configurées par notre installateur. Une structure privée non reconnue fait toujours refuser l’intégration plutôt que deviner un offset.

## Compilation et tests

SDK .NET 10 et compilateur fourni avec Windhawk :

```powershell
.\Build.ps1 -WindhawkPath "$env:LOCALAPPDATA\Programs\Windhawk"
```

Sources : `Companion/` (C#), `Windhawk/` (C++), `Assets/lolo.svg` (logo fourni), `Tests/`. Le fichier `.wh.cpp` autonome est assemblé depuis les `.inc` et est aussi livré dans `dist/`, avec la DLL compilée et le compagnon.

`scripts\Test-Ipc.ps1` contrôle les échanges locaux ; `-Command R` demande une actualisation. Le protocole est un pipe Windows limité à l’utilisateur courant et à sa session. Explorer utilise des opérations overlapped sur un thread de travail ; son thread XAML ne reçoit que des valeurs en mémoire. Le code ne lit aucun fichier d’authentification et ne conserve aucun message brut du serveur. Un Job Object ferme le serveur enfant si le compagnon se termine.

**Validation réalisée :** compilation C++/.NET, 141 tests, lecture du compte réel, notifications JSON-RPC simulées, IPC réel, actualisation manuelle, invalidation à l’arrêt, arrêt du processus enfant, désinstallation/réinstallation, chargement dans Explorer et insertion XAML. Voir [TESTS.md](../TESTS.md).

**Limites de validation :** les rendus automatisés et les captures fournies ne remplacent pas une vérification exhaustive des interactions sur plusieurs configurations Windows. Les changements effectifs de DPI/résolution, la saturation par de nombreuses applications et le redémarrage complet d’Explorer n’ont pas été testés. L’intégration est prévue pour la barre horizontale principale ; elle dépend de structures privées Windows.

## Désinstallation

```powershell
.\Uninstall.ps1
```

Ou lancer `%LOCALAPPDATA%\CodexHebdo\Uninstall.ps1`. Le script retire le compagnon, son démarrage automatique et le mod portable, en restaurant la place réservée. Pour Windhawk standard, supprimer également le mod dans son interface. Windhawk, ses symboles partagés, Codex et les sources sont conservés. Le dossier de construction et l’archive peuvent ensuite être supprimés indépendamment.

## Références étudiées

- [Documentation officielle App Server](https://learn.chatgpt.com/docs/app-server) : initialisation, limites, notifications.
- [Taskbar Folder Menus](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-folder-menus.wh.cpp) : accès XAML, réservation/restauration d’une place Grid ou StackPanel. Helpers MIT adaptés, voir `THIRD-PARTY-NOTICES.txt`.
- [rate-tray](https://github.com/nowrap/rate-tray) : transport Codex et gestion des limites ; aucune lecture de jetons reprise.
- [CodexQuota](https://github.com/JulienMaille/CodexQuota) : comportement et positionnement ; sa fenêtre rattachée à la barre n’est pas utilisée ici.


## Restriction du moteur Windhawk à Explorer

L'installateur limite désormais le moteur à l'exécutable Windows `explorer.exe` :
`AppData/Engine/settings.ini` contient `Exclude=*` et `Include=C:\Windows\explorer.exe`
(chemin Windows calculé sur la machine). Le ciblage du mod seul ne suffit pas :
Windhawk injecte normalement son moteur dans de nombreux autres processus.
Cette restriction conserve le code et le rendu du widget et évite cette injection
pour les jeux, quel que soit leur magasin ou dossier.

La configuration est créée uniquement pour le portable 1.7.3 préparé par Codex Hebdo,
ou une ancienne installation identifiée par `installation.json`, sans autre mod
(même désactivé) et sans fichier de réglages moteur préexistant. Les installations
partagées ou personnalisées restent intactes : l'installation est interrompue
avant toute modification lorsque la restriction ne peut pas être confirmée.
L'erreur indique la cause (autres mods, réglages existants, disposition inconnue).
Une autre instance Windhawk active, ou dont le chemin ne peut pas être lu,
bloque également l'installation par défaut. Cela ne surveille pas les instances
qui pourraient être lancées ultérieurement.

Pour réutiliser volontairement une configuration partagée, l'installation manuelle
accepte `-AllowSharedWindhawk`. Ce choix préserve les réglages et affiche un
avertissement ; il n'offre pas la garantie de restriction à Explorer. L'assistant
n'active jamais cette dérogation automatiquement.
Les nouveaux portables portent le marqueur `codex-hebdo-managed.txt`.

Le fichier participe à la transaction et est retiré en cas d'échec. Si Windhawk
était actif, l'installation demande son redémarrage via `-restart -tray-only`,
même si le fichier était déjà présent (notamment après une installation `-NoStart`).
Avant le démarrage, le fichier est relu et sa conformité vérifiée ; une erreur
déclenche le retour arrière. Cette vérification porte sur la configuration, pas
sur une mesure des DLL effectivement chargées dans chaque processus.
Avec `-NoStart`, redémarrer Windhawk manuellement avant de relancer les jeux.
Relancer les jeux déjà ouverts ; une modification de réglage ne garantit pas le
déchargement du moteur de processus existants. Les réglages restent dans Windhawk
après désinstallation de Codex Hebdo : les revoir avant d'ajouter d'autres mods.

Il s'agit d'une prévention générale, pas d'une confirmation du correctif d'un jeu
GOG non identifié. Aucun test du jeu signalé ni du rendu en session réelle avec
cette restriction n'a été réalisé. Windhawk indique que la restriction peut retarder
l'injection ; notre module recherche périodiquement la barre des tâches, mais la
validation interactive reste nécessaire.
Source : [documentation Windhawk](https://github.com/ramensoftware/windhawk/wiki/Injection-targets-and-critical-system-processes).
