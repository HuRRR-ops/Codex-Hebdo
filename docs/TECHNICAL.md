# Guide technique

Le dépôt contient les sources et les scripts de compilation/installation. Compiler avec `Build.ps1` avant de lancer `Install.ps1`. L’installateur unique incluant toutes les dépendances reste à préparer.

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

