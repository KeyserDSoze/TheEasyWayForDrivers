# Guardrail dei risultati CAT / SYS / DLL nel riepilogo OEM (#518)

Il reader Windows di firma CAT/INF limita a 12 coppie INF, con massimo
otto verifiche SignTool sui payload SYS/DLL. Il riepilogo
`OemReferencedPayloadAudit.Summarize` non ripeteva questi controlli:
la somma poteva accettare esiti `VerifiedMember`/`Rejected` superiori
al budget, contatori interni contraddittori e valori enum sconosciuti.

Ora il riepilogo, prima di calcolare un qualsiasi numero, verifica:
- massimo 12 INF, massimo 80 riferimenti per INF, massimo otto payload
  in ogni piano e otto risultati **VerifiedMember/Rejected** globali;
- riferimenti = presenti + mancanti + non risolti, quote non negative,
  coerenza di membership e file rinviati e nomi candidati univoci;
- un inventario dichiarato non scansionabile non può contemporaneamente
  vantare firme verificate o file presenti;
- gli enum sconosciuti e collezioni nulle sono errori, non controlli
  invisibili nel conteggio;
- rimangono validi gli esiti `NotVerified` di tutti i file (fino a
  96) di 12 INF non fidati, perché `NotVerified` non prova che
  SignTool sia stato invocato.

`OemPayloadMembershipSummaryIntegrityTests` include 8 verificati e
88 non verificati, nono verificato o respinto, oltre 12 INF, file
duplicati, contatori incoerenti, inventory non scansionabile, enum
ostili e rinvii validi. Tutti i messaggi d'errore sono generici,
senza path OEM o Device Instance ID.

Questi controlli non verificano autonomamente una firma: le
attestazioni restano esclusivamente il risultato delle API Windows
e del SignTool Windows SDK; non autorizzano installazioni.
Il test reale su Recoil 16 e il ranking OEM non staged rimangono
nelle issue #128 e #130. Firma Authenticode dell'app #283 aperta.
