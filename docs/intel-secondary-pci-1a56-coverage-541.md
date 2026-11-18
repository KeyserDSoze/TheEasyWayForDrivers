# Intel PCI VEN_1A56: PnP locale e copertura coerenti (#541)

Il prodotto già riconosce i modelli PCI Intel `8086` e `1A56`
nell'identità PnP e nella conta del companion OEM. Il detector Intel,
la Home, il supporto OEM manuale, le evidenze di versione e il
benchmark delle otto categorie usavano in alcuni rami solo `8086`.

Ora i due VEN condividono il controllo del modello completo
`PCI\\VEN_xxxx&DEV_yyyy` e della classe PCI `CC_0300/0302/0380`
(display) o `CC_0200/0280` (rete), validata sulla stessa istanza
quando WMI presenta classe generica/Unknown. Nessuna classe Windows
esplicita in conflitto viene ignorata. Nome/provider Intel non
sostituiscono l'identità primaria di un vendor straniero.

L'anteprima supporto Intel DSA resta un **collegamento manuale**;
la correlazione Windows Update usa GUID e Hardware/Compatible ID
esatti solo da fonti WUA completate e affidabili. Una scheda di rete
Intel generica non viene chiamata Killer AX1675i senza il SUBSYS
documentato. Nessun update o driver OEM installabile è creato.

La verifica è sintetica: prove Recoil 16 (#91/#136), pacchetti OEM
firmati/applicabili e ranking #128/#130 restano necessari.
