# Industrial Duck Panic

Alerte Discord pour les pilotes d'industriels EVE Online (Porpoise, Orca, Rorqual) : tackle, PANIC, perte du vaisseau, « I'm OK ».

## Structure

| Dossier | Contenu |
|---|---|
| `src/IndustrialDuckPanic.Core` | Logique testable : SSO EVE (PKCE), client ESI, lecture des gamelogs, webhook Discord, `IDP.config` chiffré |
| `src/IndustrialDuckPanic` | Application WPF (.NET 10, thème ORE) |
| `tests/IndustrialDuckPanic.Tests` | Tests xUnit (`Category=Live` = appels réels à l'ESI/Discord) |

## Développer

```powershell
dotnet test tests/IndustrialDuckPanic.Tests            # tous les tests
dotnet run --project src/IndustrialDuckPanic           # lancer l'app
dotnet run --project src/IndustrialDuckPanic -- --demo tackled   # démo sans réseau
```

Scénarios de démo : `rorqual`, `tackled`, `panic`, `wrong`, `docked`, `lost`, `error`. Ajouter `--panel status` (ou `discord`, `usage`, `legal`, `character`) pour ouvrir un panneau.

## Publier une version

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release.ps1 -Version 0.1.1
```

Résultat dans `Releases\` :
- `IndustrialDuckPanic-win-Setup.exe` : installeur à donner aux joueurs
- `IndustrialDuckPanic-win-Portable.zip` : version sans installation
- les autres fichiers (`*.nupkg`, `releases.win.json`, `RELEASES`) sont à joindre à la release GitHub pour les mises à jour automatiques

Mises à jour automatiques : renseigner l'URL du dépôt GitHub dans `AppConstants.UpdateRepositoryUrl`, puis publier chaque version comme release GitHub avec le contenu de `Releases\`.

## Application EVE

Client ID et callback (`http://localhost:8642/callback`) dans `src/IndustrialDuckPanic.Core/AppConstants.cs`. Scopes : `esi-location.read_location.v1`, `esi-location.read_ship_type.v1`, `esi-location.read_online.v1`.
