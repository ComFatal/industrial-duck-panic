# Publier une nouvelle version

Une seule commande crée l'installeur et la release GitHub. Les joueurs déjà installés reçoivent la mise à jour via Status → *Check for updates*.

## Marche à suivre

1. **Faire les changements et les commiter.** Le script refuse de publier s'il reste des fichiers non commités : la release doit correspondre exactement au code sur GitHub.

   ```powershell
   git add -A
   git commit -m "Ce qui a changé"
   ```

2. **Choisir le numéro de version**, toujours plus grand que le précédent (voir la [page Releases](https://github.com/ComFatal/rorqual-panic/releases)) :
   - correction de bug : `0.2.0` → `0.2.1`
   - nouvelle fonction : `0.2.1` → `0.3.0`

3. **Lancer la publication** depuis le dossier du projet :

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\publish-release.ps1 -Version 0.2.1
   ```

   Le script fait tout, dans l'ordre :
   1. il vérifie que tout est commité et que la version n'existe pas déjà ;
   2. il pousse `main` sur GitHub ;
   3. il télécharge la version précédente, pour que la mise à jour soit petite ;
   4. il lance les tests (**si un test échoue, rien n'est publié**) ;
   5. il compile et crée l'installeur ;
   6. il crée la release `v0.2.1` sur GitHub et y envoie les fichiers.

   À la fin, il affiche le lien de la release (≈ 2 minutes en tout).

4. **(Optionnel) Écrire les notes de version** : sur GitHub, ouvre la release → ✏️ *Edit* → décris les changements → *Update release*.

## Variante : release en brouillon

Pour vérifier avant que les joueurs la voient :

```powershell
powershell -ExecutionPolicy Bypass -File .\publish-release.ps1 -Version 0.2.1 -Draft
```

Puis, sur GitHub : Releases → la release en brouillon → *Edit* → *Publish release*.

## Si ça bloque

| Message | Solution |
|---|---|
| `Uncommitted changes` | Commite tes changements (étape 1). |
| `Tag v0.2.1 already exists` | Cette version existe déjà : prends un numéro plus grand. |
| `No GitHub login found` | Fais un `git push` une fois : la fenêtre de connexion GitHub s'ouvre. Relance ensuite le script. |
| `Tests failed` | Un test ne passe plus : corrige d'abord (`dotnet test tests/IndustrialDuckPanic.Tests`). |
| `Upload to GitHub failed` | Souvent un problème de réseau. Supprime la release incomplète sur GitHub (et le tag : `git push --delete origin v0.2.1`), puis relance. |

## Ce qui est envoyé sur GitHub

| Fichier | Pour qui |
|---|---|
| `IndustrialDuckPanic-win-Setup.exe` | les joueurs : l'installeur |
| `IndustrialDuckPanic-win-Portable.zip` | les joueurs qui ne veulent pas installer |
| `*-full.nupkg`, `*-delta.nupkg`, `releases.win.json`, `RELEASES` | le système de mise à jour automatique : ne pas les supprimer |

Le lien `https://github.com/ComFatal/rorqual-panic/releases/latest` (celui du README) pointe toujours vers la dernière version publiée.
