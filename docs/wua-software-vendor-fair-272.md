# Discovery: WUA Type=Software selezione multi-vendor (#272)

## Problema
In modalità approfondita, la prima fonte WUA Type=Software poteva
occupare tutti i **32 slot** di suggerimenti con componenti Intel.
Le fonti completate in seguito (Windows Update online e Microsoft
Update già registrato) potevano restituire titoli NVIDIA, Killer,
Realtek o altri vendor senza che fossero visibili nel riepilogo.
Si trattava di **indizi informativi**, non driver Type=Driver.

## Modifica
La raccolta avviene ora **solo dopo l'enumerazione COM completa e
verificata di ciascuna fonte**; i GUID sono deduplicati globalmente
e mantenuti in staging limitato a **3 × 4000 = 12000 righe** massime
(3 sorgenti WUA, ciascuna non più di 4000 risultati).
Un pass parziale o fallito contribuisce **zero** suggerimenti.

Dopo tutte le fonti, `SelectFair` sceglie i 32 record finali:
- Se i GUID distinti sono 32 o meno, ordine e contenuto sono invariati.
- Se superano 32, alterna un suggerimento alla volta per vendor
  riconosciuto. `Killer` ha un gruppo separato da `Intel` anche
  quando il nome contiene entrambi. Le fonti precedenti conservano
  la priorità **dentro il medesimo vendor**.
- La capienza rimane 32, con nessuno slot sprecato se è presente
  un solo vendor. `RelatedSoftwareOmitted` conta i GUID unici
  non mostrati; i riepiloghi per fonte distinguono ID acquisiti
  prima della selezione e suggerimenti effettivamente visibili.

## Sicurezza e compatibilità
Il classificatore `TryCreate` e le sue convalide di GUID, sorgente
WUA, titolo e contesto restano invariati. Non sono mai recuperati,
installati, scaricati o contati come `DriverUpdateInfo` pacchetti
Type=Software. Nessuna modifica a `QuickUpdatePolicy`,
`Aggiorna tutto`, servizi Windows o cataloghi esterni.

Nuovi test: 120 indizi Intel seguiti da NVIDIA/Killer/Realtek/
AMD/Qualcomm/Broadcom/MediaTek; 32 e 90 record mono-vendor;
dedup per GUID fra WUA e MU; input >12000 non consentito;
nessuna promozione a aggiornamento installabile.

Il benchmark sul Recoil 16 (2 contro 8) resta da verificare
sull'hardware; questo lavoro aumenta la **visibilità degli indizi
reali già restituiti dalle ricerche** e non inventa nuovi driver.
