# Inspiration til Links for Atlassian

Undersøgt 6. oktober 2026. Produktnavn: Links for Atlassian. Repository: `cmdpal-atlassian`. Aftalt placering: `C:\Workspaces\skttl\cmdpal-atlassian`.

## Udvalg og popularitet

Jeg gennemgik GitHub-projekterne fra [Command Palette-galleriet](https://microsoft.github.io/CmdPal-Extensions/) og læste kode, READMEs og workflows i seks udvalgte repositories via lokale shallow clones. Tabellen viser nogle af de højeste verificerede stjernetal. Stjerner er et mål for interesse, ikke installationer. PowerTranslator og Web Search Shortcut har også PowerToys Run-historik, og zadjii-repoet indeholder flere extensions. Tallene kan derfor ikke sammenlignes som antal aktive CmdPal-brugere.

| Repository | Stjerner ved opslag | Relevans |
| --- | ---: | --- |
| [EverythingCommandPalette](https://github.com/lin-ycv/EverythingCommandPalette) | 757 | Søgning og distribution |
| [PowerTranslator](https://github.com/N0I0C0K/PowerTranslator) | 585 | GitHub Actions med reelle releaseuploads |
| [Web Search Shortcut](https://github.com/Daydreamer-riri/CmdPal-WebSearchShortcut) | 303 | README, indstillinger og keyword-flow |
| [CmdPalExtensions](https://github.com/zadjii/CmdPalExtensions) | 117 | Flere SDK-eksempler i samme repo |
| [Microsofts GitHub-extension](https://github.com/microsoft/CmdPalGitHubExtension) | 89 | Browserlogin, navigation og onboarding |
| [LLM Extension](https://github.com/LioQing/llm-extension-for-cmd-pal) | 77 | Lille website og privatlivspolitik |
| [Visual Studio Code](https://github.com/tanchekwei/VisualStudioCodeForCommandPalette) | 76 | Yderligere populært projekt |
| [Media Controls](https://github.com/jiripolasek/MediaControlsExtension) | 47 | Tydelig installation og dokumentation |

Kodegennemgangen omfattede Everything, PowerTranslator, Web Search Shortcut, zadjii, Microsofts GitHub-extension og Media Controls. LLM og Visual Studio Code indgår som supplerende webopslag.

## Repoer, READMEs og websites

### Everything

Ét primært extensionprojekt og en særskilt hjælper, med fælles build- og pakkeindstillinger i roden. Hjælperen dækker et Everything-specifikt behov, som vores projekt ikke har. Søgningen bruger cancellation og en begrænset kø, så gamle forespørgsler kan falde bort.

README ligger i `.github/README.md`. Den begynder med logo og installationslinks til Store, WinGet, Chocolatey, Scoop og MSIX. Detaljer ligger på [GitHub Wiki](https://github.com/lin-ycv/EverythingCommandPalette/wiki). Jeg fandt ingen særskilt Pages-side i den undersøgte branch.

Den undersøgte [release-workflow](https://github.com/lin-ycv/EverythingCommandPalette/blob/b9081290825a5ff406d73d14d499a099763a14a3/.github/workflows/pushUpdates.yml) reagerer på en allerede publiceret release og opdaterer distributionskanaler. Den bygger ikke releasepakken.

### PowerTranslator

CmdPal-koden ligger under `cmdpal/`, mens roden også indeholder den ældre Run-plugin. Et nyt projekt som vores behøver kun én extension.

De overordnede READMEs viser billeder, installation og konfiguration. Den særskilte CmdPal-README er endnu kun markeret WIP i det undersøgte snapshot. Ingen særskilt dokumentationsside eller Pages-workflow fundet dér.

[build_cmdpal.yml](https://github.com/N0I0C0K/PowerTranslator/blob/3cc6318cb7da11fa69dedc2f1ba4ca5f10ca956f/.github/workflows/build_cmdpal.yml) bygger x64 og ARM64 på Windows og uploader MSIX-pakker til den GitHub Release, der udløste kørslen. Det er det mest direkte releaseeksempel til vores behov.

### Web Search Shortcut

CmdPal-projekt og tests ligger samlet, og Run-versionen er adskilt. Den bruger SDK'ets `JsonSettingsManager` og standardkommandoer/listesider. Det er god inspiration til vores indstillinger og keywords.

[README](https://github.com/Daydreamer-riri/CmdPal-WebSearchShortcut#readme) har en preview-GIF tidligt, installation med Store/WinGet/MSIX og et screenshot af konfigurationen. Det er den struktur, jeg vil bruge til vores README. Der er også en særskilt Run-README. Ingen dedikeret projektwebsite fundet i den undersøgte branch.

Den undersøgte [build-cmdpal.yml](https://github.com/Daydreamer-riri/CmdPal-WebSearchShortcut/blob/c3b69ead3b0f6909dd85ad9e2ac6cab84d1bb808/.github/workflows/build-cmdpal.yml) bygger begge arkitekturer og uploader Actions-artifacts. Dette workflow opretter ikke en GitHub Release. Det er ikke nok i sig selv til vores releasekrav.

### Microsofts GitHub-extension

Den har flere lag til API, data og UI, som understøtter et større produkt end vores. Vi kan bruge navigation og loginoplevelse som reference uden at kopiere hele arkitekturen.

[README](https://github.com/microsoft/CmdPalGitHubExtension#readme) skelner tydeligt mellem installation, almindelig brug og udvikleropsætning. [Quickstart](https://github.com/microsoft/CmdPalGitHubExtension/blob/main/docs/quickstart.md) viser browserlogin og navigation trin for trin med screenshots. Dokumentationen ligger i repoets `docs/`, og jeg fandt ingen dedikeret Pages-side i branchen.

GitHub Actions bruges til CI. Repoet har også en Azure Pipelines-buildfil; det er ikke et enkelt, generelt GitHub Actions-releaseeksempel, vi kan kopiere. Desktop-loginimplementeringen er desuden til GitHub og er ikke dokumentation for Atlassians OAuth-krav.

### Media Controls og zadjii

[Media Controls](https://github.com/jiripolasek/MediaControlsExtension#readme) er et godt dokumentationseksempel med screenshot, systemkrav, installationsmuligheder, changelog og separat kompatibilitetsguide. Forfatterens website er linket, men det er ikke dokumentation for et særskilt website til extensionen. Repoet har flere biblioteker til medieintegrationer. Vores behov passer i ét C#-projekt.

[zadjii](https://github.com/zadjii/CmdPalExtensions#readme) har et oversigts-README med downloadlinks til mange extensions og fælles buildfiler. Nyttigt som SDK-eksempelsamling; et monorepo med mange extensionprojekter passer ikke til vores ene produkt.

### Et faktisk website og GitHub Pages

[LLM Extension](https://lioqing.com/llm-extension-for-cmd-pal/) har en lille produktside med repo-link og separat [privatlivspolitik](https://lioqing.com/llm-extension-for-cmd-pal/privacy-policy/). Hostingteknologien er ikke verificeret. Dens privatlivstekst kan ikke genbruges hos os, fordi vores loginservice faktisk behandler og opbevarer refresh tokens.

[Extension-galleriet](https://github.com/microsoft/CmdPal-Extensions/blob/3a5277e449528aaa6f9d1755de93a89917b54b46/.github/workflows/deploy-pages.yml) er et verificeret GitHub Pages-eksempel. Det bygger Astro og deployer via GitHub Actions med særskilte Pages-rettigheder. Galleriets søgning og katalog forklarer behovet for Astro. Vores lille website kan være statisk HTML uden frontendafhængigheder.

## Det vi tager med

Foreslået struktur, hvor yderligere filer kun oprettes, når implementeringen bruger dem:

```text
cmdpal-atlassian/
  README.md
  LICENSE
  CHANGELOG.md
  global.json
  src/LinksForAtlassian/       C# extension fra den officielle template
  broker/                    Cloudflare Worker til OAuth
  tests/                     Små checks af søgning, URL'er og tokenflow
  docs/
    setup.md                 Udvikleropsætning og egen broker
    privacy.md               Hvilke data der gemmes og slettes
    site/                    Lille statisk Pages-side
  .github/workflows/
    ci.yml
    release.yml
    pages.yml
```

README og website skrives på engelsk, fordi extensionen skal deles offentligt. README begynder med en kort beskrivelse, en rigtig optagelse af `bb dans` og undermenuen, installation og første login. Derefter følger en tabel med `bb`, `ji` og `cf`, konfiguration, fejlfinding og links til udvikler- og privatlivsdokumentation. Ingen Store- eller WinGet-badges før de kanaler faktisk eksisterer.

Pages-siden viser download, systemkrav, et kort brugseksempel, privatlivspolitik og links til GitHub/issues. OAuth-appregistrering er udviklerdokumentation; almindelige brugere skal kun installere og logge ind. Ingen separat frontendapp eller CMS.

CI bygger extensionen og verificerer broker og de små checks. Release udløses af et versionstag, verificerer versionssammenhæng, bygger x64/ARM64 og vedhæfter installerbare pakker samt SHA256-checksummer til en GitHub Release. Build-artifacts alene opfylder ikke kravet. Signering og installation på en ren Windows-bruger skal afprøves før en release kaldes klar til almindelige brugere. Private certifikater og OAuth-secrets ligger aldrig i repo eller releaseassets.

Actions fastlåses til gennemgåede commit-SHA'er. CI har læserettigheder; kun releasejobbet har `contents: write`. Pages har sit eget deployjob og sine egne rettigheder. OAuth-brokerens deployment holdes separat fra en desktoprelease, så et nyt versionstag ikke automatisk ændrer loginservicen.

Microsoft anbefaler [Store-distribution](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/publish-extension) og beskriver også WinGet og galleriet. GitHub Releases er vores første leverancekanal. Store, WinGet og optagelse i galleriet kræver efterfølgende konkret opsætning.

## Undersøgte kodeversioner

| Lokalt snapshot | Commit |
| --- | --- |
| Everything | `b9081290825a5ff406d73d14d499a099763a14a3` |
| PowerTranslator | `3cc6318cb7da11fa69dedc2f1ba4ca5f10ca956f` |
| Web Search Shortcut | `c3b69ead3b0f6909dd85ad9e2ac6cab84d1bb808` |
| Microsoft GitHub | `16906fe4eaceda52f887f565653fe321c7a5929f` |
| Media Controls | `cce85c7170da00403beaa78af6f741d3f5da428d` |
| zadjii | `55bd660a33007592cf3e9e6f121ee1434ac0441b` |
| Gallery | `3a5277e449528aaa6f9d1755de93a89917b54b46` |

Dette er research og et konkret struktur-/udgivelsesforslag. Ingen extension, releaseworkflow eller website er bygget eller publiceret endnu.
