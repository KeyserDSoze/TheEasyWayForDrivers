# OmegaDrive — recupero delle fonti Microsoft Update e dei componenti OEM WUA (#202)

## Scenario

Nella v0.0.96 una sola voce di `Microsoft.Update.ServiceManager.Services`
con `ServiceID` COM illeggibile poteva impedire l'interrogazione della
restante lista, nascondendo **Microsoft Update già registrato**. In modalità
approfondita ciò può far saltare due ricerche driver (consigliati e
avanzati) e la terza ricerca software OEM, senza dimostrare che la
fonte sia assente.

Anche la ricerca `Type='Software'` effettuava
`SafeString(update.Title)`: il getter COM veniva chiamato prima
della funzione `SafeString`. Se una voce restituiva un'eccezione, il
pass intero falliva e gli indizi Intel PROSet, Killer Networking,
NVIDIA HD Audio/SoftwareComponent e Realtek validi scomparivano.

## Nuovo comportamento

1. `WuaSourceMetadataReader.IsMicrosoftUpdateRegistered` analizza
   **fino a 128 servizi**, controllando il GUID ufficiale Microsoft
   Update **esatto**. Se un servizio è incompleto, continua a
   esaminare gli altri. Se trova il GUID, lo considera registrato.
   Se **non** lo trova e una voce non era leggibile, riporta
   **fonte non verificata**, mai `NotRegistered`. Se legge tutti
   i servizi con GUID validi e non lo trova, il risultato è
   `NotRegistered`.
2. `WuaSourceMetadataReader.ReadSoftwareHints` legge tutti i
   `Type=Software` di un pass con esito WUA completo, fino
   al limite già previsto di **4000**. Il getter opzionale
   `Title` viene isolato **per voce**: il titolo difettoso
   non cancella gli altri titoli non installabili. Il numero di
   titoli non valutabili compare nel dettaglio del pass.
3. L'identità `UpdateID`, `Item(index)`, `Count` e l'esito
   WUA della ricerca restano **obbligatori**. Un errore di
   enumerazione o identità invalida *l'intero pass*: nessuna
   lista parziale viene inserita fra le fonti verificate.
   GUID inesistenti, titoli firmware e software generico
   sono scartati dai classificatori esistenti.
4. Nessuna registrazione di servizi Microsoft, download o
   installazione viene effettuata. I titoli `Type=Software`
   restano **solo indizi** e non entrano mai in
   `DriverUpdateInfo` / `Aggiorna tutto`.
5. Il metodo non presume la presenza di NVIDIA o dei
   driver che Driver Easy ha individuato. Un'installazione
   realistica sul PC Recoil 16 serve per verificare la
   copertura (**#91, #136**).

## Test

Copertura automatica con oggetti COM simulati:

- Microsoft Update dopo un ServiceID illeggibile o un
  `Item(index)` che fallisce.
- Fonte non verificata se l'enumerazione è incompleta,
  `NotRegistered` solo dopo lettura completa dei servizi.
- Rifiuto GUID malformati e limiti di numero servizi;
  propagazione della cancellazione.
- Ricerca `Type=Software` con NVIDIA, Intel con getter
  titolo difettoso, Killer, Realtek e software generico.
- Errori strutturali `Item/Identity` che rifiutano
  l'intero pass, limiti di 4000, firmware escluso,
  GUID obbligatori, nessuna installabilità OEM.

Da verificare su Recoil 16: numero di fonti WUA
**Microsoft Update registrate e realmente interrogate**;
GUID driver `Type=Driver` e titoli software OEM informativi,
senza copiare seriali, Device Instance ID o GUID WUA.

Questa correzione recupera possibili **fonti già accessibili**
e non crea un catalogo ufficiale NVIDIA o PCSpecialist.
