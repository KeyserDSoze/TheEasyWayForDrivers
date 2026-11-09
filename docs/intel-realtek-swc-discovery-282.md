# Discovery DCH SWC Intel e Realtek — #282

## Problema risolto
Windows espone alcune estensioni DCH come `SoftwareComponent` o
`Extension` con nome generico e provider Microsoft. Il routing di OmegaDrive
riconosceva già alcuni `SWC\\VEN_10DE` NVIDIA, ma lasciava fuori i
componenti Intel e Realtek `SWC\\VEN_8086&DEV_XXXX` e
`SWC\\VEN_10EC&DEV_XXXX` dal supporto ufficiale e dalle
evidenze delle versioni installate.

## Classificazione bounded e conservativa
`SwcVendorComponentIdentity` accetta solo una classe PnP
`SoftwareComponent`/`Extension`, il prefisso bus+vendor completo
e un codice `DEV_` composto da esattamente quattro cifre esadecimali
(terminazione o suffisso `&`). Cerca in Hardware ID e Compatible ID;
non utilizza titolo, provider, Device Instance ID o prefissi somiglianti.

- Intel: handoff al già esistente Intel Driver & Support Assistant.
- Realtek: pagina ufficiale di supporto e precedenza del vendor PnP
  sul nome del provider Intel SST nel caso Realtek.
- Entrambi: evidenza diagnostica delle versioni locali nel report Home e
  numero GUID WUA correlati per ID, quando davvero presenti.
- Un componente Intel SWC genericamente chiamato `Software Component`
  **non viene** inventato come Intel PROSet/Wi-Fi extension: occorre
  ancora l'evidenza esplicita Wi-Fi e la classe ammessa.

Questo amplia la visibilità dei **dispositivi**, non il catalogo OEM
interrogato. Nessun nuovo driver scaricabile è confermato dalla sola
identità SWC. `Type=Software` non diventa `Type=Driver`; nessun
cambiamento a `QuickUpdatePolicy`, `Aggiorna tutto`, firmare CAT,
rank SetupAPI, download o installazioni.

## Regressioni
ID case-insensitive e Compatible ID; Intel e Realtek generici;
interferenza driver provider Intel/codec Realtek; near miss sui
token VEN/DEV e classi Camera/System; fonte HTTPS allowlisted,
nessun dato privato esportato. I test simulati non sostituiscono
il collaudo Recoil 16 (#91, #136).
