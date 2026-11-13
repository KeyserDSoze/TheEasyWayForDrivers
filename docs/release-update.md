# Release and update flow

## Versioning

The source started at version **0.0.1**.

On every push to `main`, GitHub Actions first checks the latest published GitHub Release. A green pipeline increments its patch component and creates the matching `v0.0.N` release.

Failed workflow runs do not consume a public version number because the next successful run derives its version from the latest published release, not from the workflow run number.

## Build

The release workflow:

1. restores and tests the solution on Windows;
2. publishes App1.Service self-contained for `win-x64`;
3. publishes App2.Desktop self-contained for `win-x64`;
4. optionally Authenticode-signs service and desktop when code-signing secrets are configured;
5. places both publish outputs into one payload ZIP;
6. embeds that ZIP into App3.Setup;
7. publishes App3.Setup as a self-contained single-file executable;
8. optionally Authenticode-signs the setup executable;
9. generates `TheEasyWayForDrivers-Setup.exe.sha256`;
10. creates a GitHub Release containing the setup executable and SHA-256 sidecar.

No .NET runtime installation is required on the target PC.

### Optional code signing

The workflow recognizes these repository secrets:

- `CODE_SIGNING_PFX_BASE64`: base64-encoded PFX containing the Authenticode certificate and private key;
- `CODE_SIGNING_PASSWORD`: PFX password.

When both are present, the workflow signs App1.Service.exe,
App2.Desktop.exe and TheEasyWayForDrivers-Setup.exe with SHA-256 and a trusted
timestamp. If the secrets are absent, the release remains unsigned.

The certificate itself must never be committed to the repository.

## Secure update

App2.Desktop asks App1.Service to check `releases/latest`. The service accepts
an update only when the setup asset has a valid GitHub `sha256:` digest.

When the user starts the update:

1. the setup executable is downloaded to a temporary path;
2. App2.Desktop computes its SHA-256;
3. the result must match the digest returned from GitHub release metadata;
4. only then is the setup executable started elevated in `--update` mode.

A digest mismatch deletes/rejects the downloaded file.

## Rollback

Before replacing an existing installation, App3.Setup stops the running
components and stores the current Service, Desktop and Updater directories
under:

`%ProgramData%\TheEasyWayForDrivers\Rollback`

If file replacement, service configuration or service startup throws an error,
the updater attempts to restore that last-known-good snapshot, recreates the
service configuration and restarts the previous desktop version.

The rollback snapshot is intentionally kept after a successful update so it is
available for diagnostics or a future explicit rollback feature.

## Uninstall

Setup registers TheEasyWayForDrivers under the normal Windows uninstall
registry location. The uninstall command invokes the installed bootstrapper
with:

`--uninstall`

Uninstall stops/removes the Windows Service, removes tray startup entries,
removes the uninstall registration, deletes application data and schedules the
installer directory for deletion after the running uninstaller exits.


## Preserving desktop startup preferences

Fresh installs enable the desktop tray application at Windows sign-in by default.

Application updates and rollback operations do not rewrite the per-user
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` preference. This is
intentional: once the user changes "Avvia con Windows" from the desktop
settings page, a later application update must not silently turn it back on.

Uninstall still removes the startup entry as part of cleanup.


## OmegaDrive setup asset migration

v0.0.12 introduces `OmegaDrive-Setup.exe` as the canonical setup filename.
The release also publishes the byte-identical legacy
`TheEasyWayForDrivers-Setup.exe` alias for update compatibility with older
installed clients.

The v0.0.12 update client prefers the OmegaDrive asset and falls back to the
legacy asset. After the installed base has migrated, the legacy alias can be
removed in a later release.


## Build manifest and signature verification

Every release now publishes `OmegaDrive-build-manifest.json`. For each
executable artifact it records:

- role and filename;
- file size;
- SHA-256;
- file version;
- Authenticode status;
- signer and timestamp certificate subjects when available.

When signing secrets are configured, SignTool verification with `/pa` is a
hard release gate for service, desktop and setup.

Unsigned development releases remain allowed, but the manifest and GitHub
Actions step summary expose the unsigned state explicitly.

## Runtime validation asset

`Validate-OmegaDriveRuntime.ps1` is attached to each release. Run it on a
Windows 11 test machine after installation. Use `-Deep` to execute an actual
Windows Update driver search in addition to COM availability checks.


## Gate di integrità degli asset prima di pubblicare (#424)

Dopo la generazione del manifest, e prima di `gh release create`,
il workflow esegue `Test-ReleasePackage.ps1` come controllo
indipendente **bloccante** dei file già compilati:

- I quattro ruoli del manifest (`service`, `desktop`, `setup`,
  `legacy-setup-alias`) devono essere presenti una volta sola.
  Versione, commit, nomi file, dimensioni, SHA-256, file version
  e stato/firme Authenticode vengono riconfrontati con i binari reali.
- I due setup ufficiale e legacy devono essere byte-identici;
  entrambi i sidecar `.sha256` devono indicare digest reale e nome
  corretto, non il nome dell'altro asset o un digest obsoleto.
- Il `payload.zip` deve contenere **tutti e soli** i file presenti
  nelle cartelle Service/Desktop, una volta sola, con percorso relativo,
  dimensioni e hash identici. Il controllo avviene in streaming senza
  eseguire o estrarre i file OEM/Windows.
- Una suite separata `Test-ReleasePackageChecks.ps1` prova un fixture
  coerente e le manomissioni di alias, checksum, manifest/commit/versione
  e ZIP extra/mancante **prima** della build reale su Windows.

Una incoerenza impedisce la pubblicazione, invece di produrre una
release verde con allegati che non corrispondono al manifest.
Il gate verifica la coerenza del ZIP prima di incorporarlo nel setup,
ma non dimostra da solo che la risorsa incorporata funzioni in
installazione. Inoltre non sostituisce la firma Authenticode (#283),
la catena di fiducia o il collaudo end-to-end Windows (#85/#5).


## Estrazione sicura del payload ZIP incorporato nel setup (#426)

La verifica del pacchetto nel workflow (#424) controlla i file PRIMA
dell'incorporazione nel setup, ma non è sufficiente come controllo
runtime. L'installer elevato ora esegue `SetupPayloadArchive.Extract`
sulla risorsa ZIP copiata nel proprio staging, **prima** di arrestare
il servizio, creare rollback o modificare Program Files.

Il validatore fa un preflight **completo prima di creare qualsiasi
file estratto**: sono ammessi soltanto i due alberi `Service/`
e `Desktop/` (con i rispettivi EXE non vuoti), incluse le normali
voci directory create da Compress-Archive. Rifiuta entry extra,
traversal, ADS `:`, percorsi con backslash, nomi di dispositivi
Windows riservati, punti/spazi finali, caratteri di controllo,
duplicati case-insensitive e attributi symlink/reparse.

Limiti del parser runtime: ZIP massimo 512 MiB, 4096 entry,
384 MiB per file, 1 GiB di dati estratti, al massimo 16
componenti e 1024 caratteri per path ZIP. Durante la lettura
il contenuto viene contato contro la lunghezza dichiarata,
con supporto cancellazione; i file si creano solo con `CreateNew`
e la destinazione temporanea non può attraversare reparse point.
I test sintetici validano la forma reale del payload e i casi
negativi senza avviare EXE o installare driver.

Questo controllo riduce la superficie di estrazione quando
l'archivio incorporato sia stato corrotto o sostituito. Non
dimostra la firma Authenticode (#283), non sostituisce la QA
install/update/rollback su Windows 11 (#85/#5) e non modifica
la politica di installazione dei driver.


## Verifica versioni estratte prima di fermare Windows (#427)

Il gate ZIP dell'installer (#426) controlla struttura, percorsi e limiti
ma **non** certifica che il Service e il Desktop incorporati abbiano
la stessa versione del setup. Prima di questa correzione l'installer
eseguiva `StopService`/`StopDesktop`, copiava i nuovi file e leggeva
soltanto allora le versioni installate: un payload di build obsoleta
poteva interrompere l'applicazione prima di essere rifiutato.

Dopo l'estrazione ZIP e **prima di interrompere qualsiasi componente**,
`SetupEngine.ExecuteAsync` invoca ora
`SetupPayloadPreflight.Validate(staging, GetSetupVersion())`.
Il controllo legge realmente i due file
`Service/App1.Service.exe` e `Desktop/App2.Desktop.exe`, rifiuta
file mancanti, vuoti, symlink/reparse point o senza `FileVersion`
leggibile, e richiede la coincidenza `Major.Minor.Build` con la
versione canonica del setup. È la **stessa normalizzazione**
già usata dopo la copia da `ValidateInstalledPayload`, che
rimane attiva come verifica indipendente di post-installazione.

I test xUnit copiano una vera assembly PE di test (senza eseguirla)
nei due percorsi di staging e verificano entrambi validi, file
mancanti, vuoti, senza metadata, versione non corrispondente e
versione setup malformata. Su fallimento l'installer non raggiunge
`StopService`, `StopDesktop`, rollback né le modifiche a Program Files.
È un controllo di coerenza delle versioni, non una firma digitale
e non sostituisce Authenticode (#283) o i test end-to-end reali
su Windows 11 (#85).
