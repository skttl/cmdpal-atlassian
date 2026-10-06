# Links for Atlassian i PowerToys Command Palette

Design, opdateret 6. oktober 2026 med Confluence og repo-/udgivelsesresearch. Repository: `cmdpal-atlassian` i `C:\Workspaces\skttl\cmdpal-atlassian`.

## Formål

En offentlig Windows-extension, der giver hurtig adgang til Bitbucket Cloud-repositories, Jira Cloud-projekter og Confluence Cloud-spaces og sider. Brugeren søger med tastaturet og åbner links i standardbrowseren. Repositories og projekter har en undermenu med relevante destinationer.

Ingen kundespecifikke adresser, workspaces eller konti indbygges i programmet. `example-workspace` og `https://example.atlassian.net` er brugerens lokale konfiguration, ikke standardværdier i udgivelsen.

## Første version

- `bb dash` søger efter repositories med navne som Dashboard. Søgningen omfatter navn og repository-slug.
- Enter på et repository åbner links til Overview, Source, Pull requests, Branches og Pipelines.
- `ji dans` søger efter Jira-projekter på navn og projektnøgle.
- Enter på et projekt åbner Overview, Issues og projektets tilgængelige boards. Backlog tilbydes kun for boards, der understøtter den.
- Når et projekt har flere boards, vælger brugeren et board før Board eller Backlog.
- `cf dans` søger efter Confluence-spaces og sidetitler. Enter på et space giver adgang til space-overblik og søgning i dets sider. Enter på en side åbner den i browseren.
- Keywords har `bb`, `ji` og `cf` som standard og kan ændres. Præfikser matches som hele ord, så eksempelvis `jira` ikke aktiverer Jira-søgningen.
- Der kræves kun læseadgang til metadata. Extensionen udfører ingen ændringer, starter ingen pipelines og henter ingen kildekode eller issue-indhold.

PowerToys' top-level commands, fallback handlers og listesider bruges til navigation. Det præcise præfiksflow valideres tidligt i den installerede Command Palette. Kravet er, at søgeteksten efter `bb`, `ji` eller `cf` følger med ind i resultatlisten. Et eventuelt nødvendigt Enter for at åbne søgningen dokumenteres og vises ved den første afprøvning.

## Opsætning og konfiguration

Extensionens indstillinger viser Forbind Bitbucket, Forbind Jira og Forbind Confluence som separate handlinger. Hver handling åbner systemets browser og anmoder om adgang til den relevante tjeneste. Brugeren behøver kun forbinde de produkter, der bruges.

Efter Bitbucket-login vælger brugeren et tilgængeligt workspace. Efter Jira- eller Confluence-login vælger brugeren et autoriseret site. Valgene kan ændres i indstillingerne. Første version bruger ét workspace og ét site per produkt ad gangen og én forbindelse per tjeneste.

Workspaces og sites hentes fra Atlassian. Site-URL og cloud-ID samt Bitbucket-workspace-ID gemmes lokalt. De faste Cloud-login- og API-adresser er tjenesteadresser; brugerens site-adresse bruges til browserlinks.

## Teknisk løsning

To dele i samme projektleverance:

1. En C#/.NET-extension bygget på Microsofts officielle Command Palette-template og toolkit. Den indeholder indstillinger, søgbare lister, API-kald og browserlinks.
2. En lille TypeScript-loginservice på Cloudflare Workers. Den håndterer OAuth-start, callbacks, afhentning af loginresultat, tokenfornyelse og afbrydelse af forbindelsen.

Jira og Confluence bruger en delbar Atlassian OAuth 2.0 3LO-app med scopes for de understøttede funktioner. Bitbucket registreres som en separat OAuth consumer. Forbindelser og grants håndteres efter Atlassians faktiske regler; forbindelser til samme Atlassian-konto må ikke behandle et fælles refresh token som uafhængige tokens. Client IDs er offentlige identifikatorer; client secrets findes kun som Worker-secrets. Ingen slutbrugere skal oprette egne OAuth-apps eller API-tokens.

## Login og tokenbeskyttelse

Extensionen starter en kortlivet logintransaktion hos Workeren. Den modtager en tilfældig afhentningsnøgle og en browseradresse. Browserflowet bruger en særskilt, tilfældig OAuth-state, som bindes til transaktionen og valideres i callback. Afhentningsnøglen sendes aldrig til Atlassian og giver kun adgang til den ene transaktion.

Efter godkendelse bytter Workeren authorization code til tokens. Extensionen afhenter resultatet via HTTPS og en engangsudlevering og gemmer access token og en tilfældig forbindelsesnøgle beskyttet med Windows DPAPI for den aktuelle bruger. Tokens og forbindelsesnøgler placeres aldrig i browser-URL'er eller logs.

Workeren gemmer refresh tokens krypteret i en Durable Object per forbindelse. Krypteringsnøglen ligger som Worker-secret. Forbindelsesnøglen gemmes kun som hash på serversiden og kræves ved fornyelse og disconnect. Sessionen bindes til tjeneste og OAuth-app; ingen vilkårlige token-endpoints accepteres.

Durable Object serialiserer fornyelser, så samtidige requests ikke bruger samme roterende refresh token. Et nyt refresh token gemmes før svaret returneres. Access tokens udleveres til extensionen, som kalder Atlassian direkte. Loginservicen gemmer ingen repositories, projekter, boards eller søgetekster.

Ved disconnect slettes lokal cache og legitimationsoplysninger samt forbindelsens serverdata. Brugeren kan desuden tilbagekalde appadgangen hos Atlassian. Hvis en grant tilbagekaldes eller ikke længere kan fornyes, kræves nyt login.

Alle transaktioner har udløb og engangsbrug. Login- og fornyelsesendpoints får begrænsning af antal requests. API- og callback-adresser valideres mod de kendte tjenester. Der bruges mindst mulige læsescopes for de valgte endpoints.

## Søgning, cache og fejl

Repository- og projektlister hentes med pagination og gemmes i brugerens lokale appdata. Søgeresultater beregnes lokalt uden API-kald for hvert tastetryk. Match på begyndelsen af et navn eller en nøgle vises før øvrige delstrengsmatch. Store og små bogstaver behandles ens.

Cache opdateres i baggrunden, når søgningen åbnes og data er mere end 15 minutter gamle. Opdater nu giver manuel opdatering. En mislykket opdatering erstatter ikke den eksisterende cache. Cache er adskilt per konto og workspace/site og ryddes ved skift af forbindelse eller mål.

Boards hentes, når et projekt åbnes, og caches efter samme regel. Browserlinks bygges ud fra tjenestens returnerede identifikatorer og validerede basisadresse. Jira-board-ID'er opdages via API og gættes ikke ud fra projektet.

Confluence-space-metadata kan caches lokalt. Sidesøgning bruger tjenestens søge-API med en kort pause efter tastetryk og cancellation af gamle forespørgsler. Der hentes kun metadata og sidetitler, ingen sideindhold. Resultater caches kortvarigt per forespørgsel, forbindelse og site. Vis flere følger API'ets pagination. Den konkrete søgesyntaks og de nødvendige scopes verificeres mod Confluence-dokumentationen ved implementering.

Manglende login viser Forbind-handlingen. Tomme lister viser en forklaring og mulighed for at opdatere. Netværksfejl viser eksisterende cached resultater med en besked. Rate limits respekterer Retry-After. Afvist adgang vises som adgangsfejl, ikke som en tom søgning. Tokenudløb forsøger én fornyelse før brugeren bedes logge ind igen.

## Hosting og udgivelse

Udvikling og første afprøvning bruger Cloudflares workers.dev-adresse. Offentlig udgivelse bruger et stabilt HTTPS-domæne til loginservicen og registrerede callbacks. Loginservicens adresse kan konfigureres til egen hosting; en offentlig binær leveres med udgiverens tjenesteadresse.

Leverancen omfatter kildekode, bygge- og installationsvejledning samt vejledning til Worker-deployment, OAuth-appregistrering og nødvendige secrets. Cloudflare-konto, OAuth-apps, domæne og offentlig distribution ejes af udgiveren. Oprettelse af eksterne konti og offentlig deployment sker særskilt.

En installerbar pakke afprøves lokalt før offentlig distribution. Udgivelsen beskriver, hvilke tokens loginservicen behandler, hvordan de slettes, og hvordan brugeren tilbagekalder adgang.

GitHub Actions har separate workflows til CI, release og GitHub Pages. Versionstags udløser build af x64 og ARM64 og upload af installerbare pakker samt SHA256-checksummer til GitHub Releases. Versionsnumre skal stemme mellem tag og pakkemanifest. Signering konfigureres med secrets og afprøves ved installation på en ren Windows-bruger. Private certifikater udgives aldrig. Broker-deployment holdes separat.

README på engelsk viser brug, en rigtig optagelse, installation, første login og keywords. En lille statisk GitHub Pages-side viser download, brug og privatlivspolitik. Ingen frontendframework er nødvendigt. Udvikleropsætning og egen hosting dokumenteres særskilt. Store og WinGet kan tilføjes, når deres distributionsopsætning er klar.

Repo- og dokumentationsvalg er beskrevet i `2026-10-06-cmdpal-extension-research.md` ved siden af dette dokument.

## Verifikation

- En lille runnable test verificerer præfiksparsing, match/rækkefølge og korrekt URL-escaping.
- Worker-tests verificerer forkert state, udløbet transaktion, engangsafhentning, forkert forbindelsesnøgle og samtidige tokenfornyelser.
- Et lokalt build verificerer kompatibilitet med det aktuelle Command Palette SDK.
- Manuel afprøvning i Command Palette verificerer `bb dash`, `ji dans`, `cf dans`, undermenuer, login, genstart og opdatering med netværksfejl.
- Rigtig OAuth og API-adgang verificeres, når udgiverens apps og hosting er opsat. Et build alene tæller ikke som bevis for, at login virker.

## Afgrænsning

Første version understøtter Cloud. Data Center, flere samtidige konti, issue-søgning, egne dashboards, favoritter og skrivehandlinger er ikke med. De tilføjes kun efter et konkret behov.

## Kilder

- [Microsoft: Extension-modellen](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/extensibility-overview)
- [Atlassian: Jira OAuth 2.0 3LO](https://developer.atlassian.com/cloud/jira/platform/oauth-2-3lo-apps/)
- [Atlassian: Bitbucket OAuth](https://support.atlassian.com/bitbucket-cloud/docs/use-oauth-on-bitbucket-cloud/)
- [Cloudflare: Workers-priser](https://developers.cloudflare.com/workers/platform/pricing/)
- [Cloudflare: workers.dev og produktionsdomæner](https://developers.cloudflare.com/workers/configuration/routing/workers-dev/)
