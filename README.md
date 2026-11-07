# GestionNotes – Mise à jour automatique

Application WPF (MVVM) avec un mécanisme de mise à jour fait maison (sans ClickOnce ni MSIX). Les nouvelles versions sont déposées dans un dossier réseau partagé Windows. L'application détecte la nouvelle version, la copie en local, puis un programme auxiliaire (`Updater.exe`) remplace les fichiers et relance l'application.

**Auteur :** Derdeyn Thomas, Farhane Monaim

## 1. Chemin UNC utilisé

```
\\10.77.64.45\GestionNotes-Depot
```

Il est défini dans `GestionNotes/appsettings.json` (clé `UpdateFile`) :

```json
{
  "AppSettings": {
    "Titre": "Mon application",
    "UpdateFile": "\\\\10.77.64.45\\GestionNotes-Depot"
  }
}
```

Pour utiliser une autre VM, il suffit de changer cette valeur dans le `appsettings.json` qui est à côté de `GestionNotes.exe`. Il n'y a rien à recompiler.

## 2. Configuration du partage (sur la VM)

1. Créer le dossier `GestionNotes-Depot` (par exemple `C:\GestionNotes-Depot`).
2. Clic droit sur le dossier > **Propriétés** > onglet **Partage** > **Partage avancé**.
3. Cocher **Partager ce dossier** et garder le nom `GestionNotes-Depot`.
4. Cliquer sur **Autorisations** (permissions de **partage**) : donner **Lecture** aux clients et **Modification** au compte qui publie les versions.
5. Onglet **Sécurité** (permissions **NTFS**) : donner **Lecture et exécution** aux clients et **Modification** au compte qui publie.

Les deux niveaux de permissions s'additionnent, mais c'est le **plus restrictif** qui s'applique. Les clients n'ont besoin que de la lecture : ils lisent `version.json` et copient le zip.

### Valider l'accès depuis le poste de travail

1. Ouvrir l'Explorateur de fichiers.
2. Taper `\\10.77.64.45\GestionNotes-Depot` dans la barre d'adresse.
3. Créer un fichier de test (ou en copier un) dans le dossier, puis le rouvrir pour vérifier qu'on peut le lire.

Si l'accès échoue, vérifier dans l'ordre :
- la connexion : `ping 10.77.64.45` ;
- que le poste et la VM sont sur le même VLAN ;
- le pare-feu Windows de la VM (le **Partage de fichiers et d'imprimantes** doit être autorisé) ;
- que le service **Serveur** est démarré sur la VM ;
- les permissions de partage et NTFS.

### Contenu du dépôt réseau

```
GestionNotes-Depot\
  version.json
  GestionNotes-2.2.32.zip
```

## 3. Publier une nouvelle version

Dans Visual Studio :

1. **Changer le numéro de version.** Ouvrir `GestionNotes/GestionNotes.csproj` et modifier les trois lignes (par exemple pour la 2.2.32) :
   ```xml
   <Version>2.2.32</Version>
   <AssemblyVersion>2.2.32.0</AssemblyVersion>
   <FileVersion>2.2.32.0</FileVersion>
   ```
   C'est `AssemblyVersion` que l'appli lit pour connaître sa version, il faut donc changer les trois.
2. **Publier.** Clic droit sur le projet `GestionNotes` > **Publier** > choisir le profil de dossier (Release) > **Publier**.
3. **Vérifier le dossier publié.** Il doit contenir `GestionNotes.exe`, `Updater.exe`, `Updater.dll` et `Updater.runtimeconfig.json`. Sans `Updater.dll`, la mise à jour ne pourra pas se relancer.
4. **Créer le zip.** Sélectionner **le contenu** du dossier publié (pas le dossier lui-même), clic droit > Envoyer vers > Dossier compressé, et le nommer `GestionNotes-2.2.32.zip`.
5. **Créer le manifeste.** Écrire un fichier `version.json` avec la même version que celle du `.csproj` :
   ```json
   {
       "version": "2.2.32",
       "fichier": "GestionNotes-2.2.32.zip"
   }
   ```
6. **Déposer dans le partage.** Copier `GestionNotes-2.2.32.zip` et `version.json` dans `\\10.77.64.45\GestionNotes-Depot` en remplaçant les anciens fichiers.

Il n'y a pas besoin de recompiler ni de redéployer le client : il lit le `version.json` à chaque démarrage.

### Format du manifeste

- `version` : la version disponible.
- `fichier` : le nom exact du zip dans le dépôt.

La version du manifeste doit être la même que celle de l'appli contenue dans le zip, sinon l'appli proposerait la mise à jour en boucle.

## 4. Comment fonctionne la mise à jour

1. Au démarrage, `ServiceUpdate` vérifie que le dossier réseau est accessible, puis lit `version.json` et le désérialise avec `System.Text.Json`.
2. Il compare la version du manifeste à celle de l'appli avec la classe `System.Version` (et pas avec du texte : avec du texte, `1.10` serait plus petit que `1.2`).
3. S'il y a une version plus récente, l'appli demande à l'utilisateur s'il veut mettre à jour. S'il refuse, l'appli démarre normalement.
4. Si l'utilisateur accepte :
   - on vérifie que le zip existe dans le dépôt ;
   - on le copie dans `%TEMP%\GestionNotes\update-<version>\` ;
   - on vérifie que la copie est complète (même taille que l'original).
5. L'appli vérifie que `Updater.exe`, `Updater.dll` et `Updater.runtimeconfig.json` sont présents, en lance une copie depuis le dossier temporaire avec ces arguments, puis se ferme :

   | Argument | Contenu |
   |---|---|
   | `--pid` | numéro du processus de GestionNotes |
   | `--zip` | chemin du zip copié en local |
   | `--install-dir` | dossier d'installation de l'appli |
   | `--exe` | nom de l'exécutable à relancer |

6. `Updater.exe` :
   - attend la fin complète du processus GestionNotes (30 secondes maximum) ;
   - extrait le zip dans `%TEMP%\GestionNotes\updater-extract` ;
   - copie les fichiers dans le dossier d'installation (3 essais si un fichier est encore verrouillé) ;
   - ne touche **pas** à `appsettings.json`, pour garder la configuration locale ;
   - relance GestionNotes, puis se termine.

Tout ce que fait l'Updater est écrit dans `%TEMP%\GestionNotes\updater.log`.

**Pourquoi un Updater séparé :** une application ne peut pas remplacer ses propres fichiers pendant qu'elle tourne, parce que Windows les verrouille. Il faut un autre programme qui attend sa fermeture, remplace les fichiers, puis la relance. On lance une copie de l'Updater depuis le dossier temporaire pour qu'il puisse aussi remplacer ses propres fichiers.

**Pourquoi copier le zip en local :** pour que le remplacement ne dépende plus du réseau. Si le partage tombait pendant le remplacement, l'installation serait à moitié mise à jour et inutilisable. Avec une copie locale vérifiée, si la copie échoue on le sait avant de fermer l'appli.

### Gestion des erreurs

| Situation | Ce qui se passe |
|---|---|
| Dossier réseau inaccessible | Message d'erreur, l'appli démarre |
| `version.json` absent | Message d'erreur, l'appli démarre |
| Manifeste invalide (JSON cassé, champ manquant, version illisible) | Message d'erreur, l'appli démarre |
| Aucune nouvelle version | Rien, l'appli démarre normalement |
| L'utilisateur refuse | L'appli démarre avec la version installée |
| Zip absent du dépôt | Message d'erreur, l'appli ne se ferme pas |
| Copie du zip échouée | Message d'erreur, l'appli ne se ferme pas |
| Fichiers de l'Updater manquants | Message d'erreur, l'appli ne se ferme pas |
| Zip impossible à extraire | Erreur dans `updater.log`, l'ancienne version est relancée |

## 5. Refaire les tests

### Préparation

### Préparation

1. **Installer la version de départ (2.2.30)** sur le poste client :
   - mettre `2.2.30` dans le `.csproj` (`Version`, `AssemblyVersion`, `FileVersion`) ;
   - publier avec Visual Studio (voir section 3) ;
   - copier le contenu du dossier publié dans `C:\GestionNotes-Install`.
   Éviter `C:\Program Files` : l'Updater n'aurait pas le droit d'y écrire.
2. **Publier la version 2.2.32** dans le dépôt :
   - mettre `2.2.32` dans le `.csproj` et publier avec Visual Studio ;
   - créer `GestionNotes-2.2.32.zip` et `version.json` comme décrit en section 3 ;
   - les copier dans `\\10.77.64.45\GestionNotes-Depot`.
3. Toujours lancer l'appli en **double-cliquant sur `GestionNotes.exe`** dans `C:\GestionNotes-Install` (pas depuis Visual Studio). La version s'affiche dans la fenêtre.

### Scénario 1 : mise à jour réussie

1. Installée : 2.2.30. Dépôt : 2.2.32.
2. Lancer `GestionNotes.exe` : un message annonce la mise à jour 2.2.32.
3. Répondre **Oui**, puis valider le message qui annonce la fermeture.
4. **Résultat attendu :** l'appli se ferme, les fichiers sont remplacés, et elle redémarre en version 2.2.32.

### Scénario 2 : aucune mise à jour nécessaire

1. Installée : 2.2.32 (celle obtenue après le scénario 1). Dépôt : 2.2.32.
2. Lancer `GestionNotes.exe`.
3. **Résultat attendu :** aucun message de mise à jour, l'appli fonctionne normalement.

### Scénario 3 : partage inaccessible

1. Dans `C:\GestionNotes-Install\appsettings.json`, remplacer `UpdateFile` par un chemin invalide, par exemple `\\\\10.77.64.45\\Inexistant`.
2. Lancer `GestionNotes.exe`.
3. **Résultat attendu :** un message indique que le dossier réseau est inaccessible, puis l'appli s'ouvre et reste utilisable.
4. Remettre le bon chemin dans `appsettings.json`.

### Scénario 4 : refus de la mise à jour

1. Réinstaller la version 2.2.30 dans `C:\GestionNotes-Install` (étape 1 de la préparation). Dépôt : 2.2.32.
2. Lancer `GestionNotes.exe` et répondre **Non** à la mise à jour.
3. **Résultat attendu :** l'appli démarre et la version affichée reste 2.2.30.

### Scénario 5 : archive absente

1. Garder la 2.2.30 installée. Dans `version.json` du dépôt, mettre un nom de zip qui n'existe pas :
   ```json
   { "version": "2.2.32", "fichier": "GestionNotes-9.9.9.zip" }
   ```
2. Lancer `GestionNotes.exe` et accepter la mise à jour.
3. **Résultat attendu :** un message dit que l'archive est introuvable. L'appli ne se ferme pas et aucun fichier n'est modifié.
4. Remettre le bon `version.json` dans le dépôt.

### En cas de problème

- L'appli se ferme après le message mais ne redémarre pas : lire `%TEMP%\GestionNotes\updater.log`. S'il n'existe pas, `Updater.exe` n'a pas démarré : vérifier que `Updater.dll` et `Updater.runtimeconfig.json` sont dans `C:\GestionNotes-Install`.
- La mise à jour est proposée en boucle : la version du `version.json` n'est pas la même que celle de l'appli dans le zip.
- « Manifeste incomplet » : les propriétés doivent s'appeler `version` et `fichier`.

## 6. Captures d'écran

**Partage réseau accessible**

<img width="1536" height="864" alt="partage" src="https://github.com/user-attachments/assets/abd690cf-fcee-45aa-b292-6104b37b2a24" />

**Version avant la mise à jour (2.2.30)**

<img width="1536" height="863" alt="avant" src="https://github.com/user-attachments/assets/3711e2e7-e74f-47fb-9873-f21f5ce65734" />


**Détection de la nouvelle version**

<img width="1536" height="863" alt="detection" src="https://github.com/user-attachments/assets/624c503c-3954-42e1-a277-e05aaa0922b4" />

**Version après la mise à jour (2.2.32)**

<img width="1536" height="863" alt="apres" src="https://github.com/user-attachments/assets/01b462de-d047-4402-b86c-20f44b8958fd" />

## 7. Modification

Le code de l'application a été modifié pour que l'affichage de la version de l'application soit dynamique (elle est lue depuis l'assembly au lieu d'être écrite en dur).
