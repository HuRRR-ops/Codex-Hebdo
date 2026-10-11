# Guide technique

La release 1.2.0 fournit un installateur automatique Windows x64. Il inclut .NET, télécharge les dépendances manquantes depuis leurs releases officielles et prépare Windhawk portable. Une connexion au compte reste interactive si nécessaire.

Les instructions ci-dessous concernent la compilation et l’installation manuelles. Compiler avec `Build.ps1` avant de lancer `Install.ps1`.

## Installateur automatique

- Source : `Installer/`, construction : `scripts/Build-Installer.ps1` après `Build.ps1`.
- Construction avec .NET 10 et 7-Zip installé sur la machine de compilation.
- Le build récupère 7-Zip 26.04 et ses sources avec vérification SHA-256. Les binaires, licences et sources correspondantes sont inclus dans le paquet.
- Le runtime .NET est publié en mode autonome. La machine cible n’a besoin ni du SDK, ni d’un runtime installé séparément.
- Les dépendances téléchargées sont épinglées : Windhawk 1.7.3 et Codex CLI 0.162.1. SHA-256 obligatoire ; signature Authenticode également vérifiée pour Windhawk.
- Windhawk portable compatible et Codex existants sont réutilisés. Windhawk standard ou une version portable incompatible est préservé et signalé, sans écrasement.
- Codex installé par l’assistant reste dans `%LOCALAPPDATA%\Programs\CodexHebdo-Codex`. Aucun changement global de PATH.
- Cache de téléchargement : `%LOCALAPPDATA%\CodexHebdo-Setup\Downloads`. Journal : `%LOCALAPPDATA%\CodexHebdo-Setup\1.2.0\setup.log`. La sortie des commandes de connexion Codex n’est pas enregistrée.
- Une annulation est possible avant l’application des fichiers du widget. Pendant cette phase finale, l’annulation est désactivée.
- L’application est ajoutée à la liste des applications installées de Windows. La désinstallation conserve Codex et Windhawk.
- L’exécutable de l’installateur n’est pas signé par un certificat de signature de code.

### Vérifications de la release 1.2.0

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

Les symboles de `taskbar.dll` sont préparés par PowerShell avant activation. Le mod désactive explicitement les serveurs de symboles et caches en ligne dans Explorer. Après une mise à jour Windows changeant `taskbar.dll`, relancer `Install.ps1` pour préparer les nouveaux symboles. Une structure privée non reconnue fait refuser l’intégration plutôt que deviner un offset.

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

