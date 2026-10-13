# Analisi pacchetti OEM — fase 1 (#66)

## Inventario implementato

| Fonte | Dato disponibile | Classificazione sicura | Limite |
|---|---|---|---|
| Dell DriverPackCatalog | Modello, release, OS dichiarato, percorso pack | DeploymentPack | Non singolo driver; nessun Hardware ID confermato |
| HP CMSL SoftPaq | Id, titolo, versione, data, categoria Driver filtrata | DriverMetadata | Metadata, non applicabilità per-device |
| Lenovo Client Scripting | Id, nome, PackageType=2 filtrato | DriverMetadata | MachineType e singolo dispositivo non verificati qui |
| ASUS/Acer/MSI/GIGABYTE/Surface/Intel/NVIDIA/AMD e altri | Handoff/supporto documentato | Unknown senza attestazione vendor | Non esiste una API generale verificata nel progetto |

La classe `OemPackageAssessment` converte **soltanto record già filtrati dai parser ufficiali** in un risultato comune. Non deduce il tipo di pacchetto da parole nel nome: la categoria BIOS/firmware è esclusa dal percorso generico **solo quando esplicitamente segnalata dall'integrazione**. Fonte esterna non autenticata e stringhe descrittive non sono prove crittografiche.

## Distinzioni obbligatorie

- **Metadati OEM**: il vendor dichiara un contenuto del catalogo.
- **Potenziale pertinenza**: associazione per modello di computer, non per Hardware ID.
- **Payload verificato**: servono firma CAT, membership dei file, policy kernel e verifica dei payload.
- **Installabilità**: richiede validazione Windows SetupAPI, integrità, dipendenze e QA hardware.

Ogni risultato della fase 1 ha `IsInstallable=false`, `SignatureVerified=false`, `DeviceMatchVerified=false`. Nessuna estrazione CAB/MSI/EXE, download o installazione è stata introdotta.

## Next steps

1. Integrare l'assessment nell'adapter e nella UI OEM (non effettuato nella fase 1).
2. Contratto per firmware/BIOS basato su categoria ufficiale esplicita e validata, non matching del titolo.
3. Definire inventario bounded e read-only degli archivi OEM locali, con difese da traversal, zip bomb, symlink e decompressione.
4. Migliorare la verifica per-device tramite INF/Hardware ID e SetupAPI (issue #58, #46).
5. Eseguire CI e QA Windows 11 reale (#5) prima di modificare le policy.
