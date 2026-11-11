# OmegaDrive — riserva di lookup SetupAPI per Hardware ID PnP (#359)

## Difetto risolto

Il servizio `WmiDriverInventory` integra gli Hardware ID e i
Compatible ID di ogni `Win32_PnPEntity` con proprietà SetupAPI
**della stessa istanza PnP**. Questa ricerca è facoltativa,
read-only e da tempo limitata a **2.048 lookup per scansione**.

Il contatore precedente consumava indistintamente un tentativo quando
mancava il CompatibleID oppure l'HardwareID. Su grandi inventari
con centinaia/migliaia di nodi con HardwareID già specifici ma
CompatibleID vuoti, il budget può esaurirsi prima che vengano
enumerati NVIDIA RTX, Intel Killer, Realtek audio o SoundWire
con HardwareID WMI solo generici. Il successivo matching WUA/INF
non può associare un modello VEN+DEV che non è mai stato acquisito
dalla stessa istanza.

## Nuova priorità, limite immutato

`PnpNativeRecoveryBudget` governa un singolo scan:
- **massimo totale: 2.048 lookup** su dispositivi WMI esistenti;
- i lookup richiesti **solo** per CompatibleID mancante sono
  consentiti finché il totale è inferiore a **1.792**;
- i **256 tentativi finali** restano disponibili **soltanto** ai
  dispositivi con HardwareID mancante o incompleto, anche se mancano
  contemporaneamente i CompatibleID;
- un dispositivo che non ha nulla da recuperare non consuma budget;
  se SetupAPI non è disponibile, non vengono tentati lookup.

I limiti di input, le verifiche del PnP DeviceID e i decoder
di `WindowsSetupApiPnpIdentityReader` restano invariati.
`PnpSetupApiIdentity.Merge` continua a preservare identità
HardwareID WMI complete, verificare bus/vendor/funzione per
qualsiasi modello nativo e a rifiutare i modelli di altri dispositivi.
Non si legge il registro di sistema di altre istanze, non si
eseguono driver e non si modifica Windows Update.

Per inventari ordinari (poche centinaia di dispositivi),
**nessun lookup prima della soglia 1.792 è sacrificato**.
Con oltre 2.048 nodi che richiedono solo CompatibleID,
questi non possono più sottrarre il budget agli HardwareID
di dispositivi successivi. Non è una garanzia assoluta per
inventari con oltre 2.048 dispositivi con HardwareID già incompleto:
è un limite esplicito conservativo.

## Prove automatiche

I test Service simulano 10.000 richieste CompatibleID-only,
la riserva esatta di 256, casi misti, richieste assenti,
2048 HardwareID realmente mancanti e normal inventories.
Per i casi NVIDIA PCI, Intel Killer PCI, HDAUDIO Realtek e
SOUNDWIRE SDCA verificano che `PnpSetupApiIdentity.Merge` usi
solo un modello nativo coerente con WMI e che il match WUA
resti per **Hardware ID esatto**, senza promuovere token
generici a falsi aggiornamenti.

I test sono sintetici: **non** dimostrano che il PCSpecialist
Recoil 16 superi 1.792 dispositivi o che v0.0.219 trovi
nuove offerte driver. Le issue #91, #99, #111, #136 e #85
restano aperte fino al benchmark hardware reale.
