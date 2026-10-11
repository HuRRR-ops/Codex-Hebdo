# Codex Hebdo

Compteur natif Windows 11 x64 : logo fourni à gauche et **Hebdo au-dessus du pourcentage restant**, avec le nombre de réinitialisations à droite, de la même taille que le pourcentage (`↻0`). Sans délai ni barre. Largeur ajustée automatiquement, police Segoe UI. Vert > 50 %, jaune > 20 %, rouge ≤ 20 %. Le logo suit le thème Windows.

Réinitialisations : rouge à zéro, vert dès un, gris (`↻--`) si inconnu ou périmé. La valeur exacte figure aussi dans l’infobulle (affichage compact `99+` au-delà de 99). Le compteur consulte uniquement `rateLimitResetCredits.availableCount` ; aucun clic ne consomme une réinitialisation. Les notifications sans ce champ ne prolongent pas sa fraîcheur, limitée à 75 secondes.

Le mod ajoute un enfant à `SystemTrayFrameGrid`, avant les icônes de notification. Il réserve une colonne Auto dans un Grid, ou une position dans un StackPanel. Aucun overlay, fenêtre de widget ou icône de quota. Windhawk possède sa propre icône de gestion.

## Utilisation

- **Double-clic uniquement** : ouvre les options sans effacer les statistiques. La fenêtre ne se redimensionne pas manuellement : elle adapte sa taille à l’onglet et au DPI. Clic simple et clic droit : aucun menu. Survol : quota et date/heure locale.
- Textes indépendants : couleur automatique ou personnalisée, gras, italique, taille et visibilité.
- Logo PNG/SVG personnalisé, teinte, taille, visibilité et restauration du logo d’origine. L’import conserve les proportions et la transparence ; une copie PNG est intégrée aux préférences.
- Espacements, centrage du titre et palette des seuils. Les grandes polices sont réduites si nécessaire pour tenir dans la barre des tâches.
- **Général → Langue / Language** : Français ou English, y compris le widget, les infobulles et les détails.
- Aperçu en direct ; **Appliquer** enregistre, **Annuler/Fermer** restaure la dernière version enregistrée. Préférences locales dans `preferences.json`, conservées lors d’une mise à jour.
- Général : actualisation manuelle et démarrage avec Windows. Aucun clic ne consomme de réinitialisation.
- Sans données, en erreur, après expiration ou après 75 secondes sans mesure : **Hebdo / --**.
- Le compagnon conserve `codex app-server --stdio`, initialise JSON-RPC, écoute les notifications et relit toutes les 30 secondes. En erreur : 30, 60, 120, 240, puis 300 secondes ; le bouton Actualiser peut demander une reprise anticipée.
- Seule une fenêtre de **10 080 minutes** du bucket Codex est acceptée, qu’elle soit `primary` ou `secondary`. Les réponses ambiguës sont rejetées. Restant = `100 - usedPercent` ; aucun quota Session n’est affiché.

## Installation

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

**Validation réalisée :** compilation C++/.NET, 102 tests, lecture du compte réel, notifications JSON-RPC simulées, IPC réel, actualisation manuelle, invalidation à l’arrêt, arrêt du processus enfant, désinstallation/réinstallation, chargement dans Explorer et insertion XAML. Voir `TESTS.md`.

**Reste à confirmer visuellement :** rendu, infobulle et clic physique ; le service de capture Windows de cette session était indisponible. Les changements effectifs de DPI/résolution, la saturation par de nombreuses applications et le redémarrage complet d’Explorer n’ont pas été testés. L’intégration est prévue pour la barre horizontale principale ; elle dépend de structures privées Windows.

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

## Interface / English

Double-click the widget to open settings. Single-click and right-click do not open a menu.
Choose **General → Language → English** to translate the widget, settings, tooltips and quota details.
Changes preview live; **Apply** saves them and **Cancel/Close** restores the last saved settings.
Custom logos accept local PNG and static SVG files, preserving transparency and aspect ratio.
The application does not use reset credits; the counter is informational.

Preferences and imported logos are personal data, stored locally and excluded from the source release.
A complete single-file installer is not included yet. This repository currently provides source code and build/install scripts.
