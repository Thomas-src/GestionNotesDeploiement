# GestionNotes – Mise à jour automatique

Application WPF (MVVM) avec un mécanisme de mise à jour fait maison (sans ClickOnce ni MSIX). Les nouvelles versions sont déposées dans un dossier réseau partagé Windows.

**Auteur :** Derdeyn Thomas, Farhane Monaim

## 1. Chemin UNC utilisé

```
\\10.77.64.45\GestionNotes-Depot
```

Il est défini dans `GestionNotes/appsettings.json` (clé `UpdateFile`).

## 2. Configuration du partage (sur la VM)

1. Créer le dossier `GestionNotes-Depot`.
2. Clic droit > Propriétés > Partage avancé > partager le dossier sous le nom `GestionNotes-Depot`.
3. Permissions de partage : Lecture pour les clients, Modification pour celui qui publie.
4. Permissions NTFS : Lecture pour les clients, Modification pour celui qui publie (le plus restrictif des deux niveaux s'applique).
5. Depuis le poste client, ouvrir le chemin UNC dans l'Explorateur et créer un fichier de test pour valider l'accès.

Le dépôt contient :

```
GestionNotes-Depot\
  version.json
  GestionNotes-2.2.32.zip
```

## 3. Publier une nouvelle version

1. Lancer le script à la racine du repo :
   ```powershell
   .\publish-version.ps1 -Version 2.2.32
   ```
   Il publie l'appli, crée `dist\GestionNotes-2.2.32.zip` et `dist\version.json`.
2. Copier ces deux fichiers dans `\\10.77.64.45\GestionNotes-Depot` en remplaçant les anciens.

Aucune recompilation côté client n'est nécessaire.

Format du manifeste :

```json
{
    "version": "2.2.32",
    "fichier": "GestionNotes-2.2.32.zip"
}
```

La version du manifeste doit être la même que celle de l'appli dans le zip.

## 4. Comment fonctionne la mise à jour

1. Au démarrage, l'appli lit `version.json` sur le partage et le désérialise (`System.Text.Json`).
2. Elle compare la version du manifeste à la sienne avec `System.Version` (pas avec du texte, donc 1.10 > 1.2).
3. Si une version plus récente existe, elle demande à l'utilisateur d'accepter ou de refuser.
4. Si l'utilisateur accepte, le zip est copié dans `%TEMP%\GestionNotes\update-<version>` et la taille de la copie est vérifiée.
5. L'appli lance `Updater.exe` (en lui passant le PID, le zip local, le dossier d'installation et le nom de l'exe), puis se ferme.
6. `Updater.exe` attend la fin du processus GestionNotes, extrait le zip, remplace les fichiers (sauf `appsettings.json`) et relance l'appli.

**Pourquoi un Updater séparé :** une appli ne peut pas remplacer ses propres fichiers pendant qu'elle tourne, Windows les verrouille.

**Pourquoi copier le zip en local :** pour que le remplacement ne dépende pas du réseau. Si la copie échoue, on le sait avant de fermer l'appli.

**Erreurs gérées :** dossier réseau inaccessible, `version.json` absent ou invalide, aucune mise à jour, zip absent, copie échouée, refus de l'utilisateur. Dans tous ces cas, un message s'affiche et l'appli reste utilisable (elle ne se ferme pas).

## 5. Tests réalisés

| Scénario | Résultat |
|---|---|
| 1. Installée 2.2.30, dépôt 2.2.32, mise à jour acceptée | L'appli redémarre en 2.2.32 |
| 2. Installée 2.2.32, dépôt 2.2.32 | Aucune mise à jour |
| 3. Partage inaccessible | Message d'erreur, appli utilisable |
| 4. Mise à jour refusée | L'appli continue en 2.2.30 |
| 5. Zip absent du dépôt | Message d'erreur, l'appli ne se ferme pas |

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
