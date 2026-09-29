# Bretzel — il coniglio — design

Data: 2026-09-29. Stato: approvato in chat (cervello separato con base astratta, comportamenti scelti);
da rileggere prima del piano.

## Obiettivo

Un secondo animaletto per l'app: Bretzel, il coniglio ariete di Valentina. Stessa app di Zaira, uno o
l'altro (`--cat=bretzel`), costruito in modo che domani le due specie possano divergere molto o stare
sullo schermo insieme.

Prima versione di Bretzel: saltelli in giro, seduto a pagnotta, si lava il muso, naso che fremica,
orecchie che ballonzolano, mangia dalla ciotola, si fa trascinare e accarezzare, binky e corse pazze,
flop sul fianco quando è rilassato. Resta sul pavimento (barra delle applicazioni): niente finestre.

## Scelte

| Tema | Scelta |
|---|---|
| Modello | `C:\Users\riccardo.beninatto\Downloads\cute+rabbit+3d+model.glb` (Tripo, riggato, nessuna animazione, texture 4K). Il colore gli assomiglia già: niente `material_colors` |
| Selezione | cartella `cats/bretzel/` con `profile.json`, `--cat=bretzel`; nel profilo un nuovo campo `species` (`cat` / `rabbit`) sceglie il cervello |
| Cervello | `PetBrain` astratto con il comune, `CatBrain` e `BunnyBrain` che lo estendono (non un cervello solo con interruttori: le specie devono poter divergere) |
| Salvataggi | uno per animale: `save-<cartella>.json`; il vecchio `save.json` diventa quello di Zaira al primo avvio |
| Testi | nome nei menu, nel tooltip e nell'overlay di debug presi dal profilo; le descrizioni degli stati le dà il cervello (Zaira "seduta", Bretzel "seduto") |

## 1. Cervello: `PetBrain` + specie

Oggi `CatBrain` (785 righe) è tutto insieme. Si divide così, **senza cambiare niente di come si comporta
Zaira**: i 140 test e i self-test devono passare identici dopo lo spostamento.

**`PetBrain` (astratto, `src/Core/PetBrain.cs`)** — quello che ogni animale fa allo stesso modo:
- API verso il gioco: `Update`, `OnGrab`, `OnRelease`, `OnPetting`, `Cheer`, `Notice`, `Summon`, i comandi
  dei self-test (`SitFor`, `StandFor`, `ExploreTo`); proprietà `State`, `Goal`, `Action`, `Facing`, `Emote`,
  `Happy`, `Cursor`, `JumpTarget`, `EatReach`; `Describe(state)` per l'overlay.
- Il ciclo del frame: timer, bisogni, in braccio, in volo, passo del corpo, atterraggio, muro.
- Gli stati comuni: in braccio, in volo, atterra, coccole, dorme, mangia, va alla ciotola / al bocconcino /
  in esplorazione (viaggio con `Navigator`), gironzola, seduto, fermo.
- Utilità: `Toward`, `Enter`, `Destination`, `Current`, `PickExplore`.

Punti in cui ogni specie decide per sé (astratti o virtuali):
- `Decide` — il prossimo comportamento dai bisogni.
- `Gait(speed)` e le velocità di viaggio — Zaira passo/trotto/andatura/galoppo, Bretzel saltelli/corsa.
- `RestingAction` — Zaira seduta → sdraiata → pagnotta; Bretzel pagnotta, ogni tanto si lava.
- `DoZoomies` — Zaira galoppa; Bretzel corre con i binky in mezzo.
- `SleepAction` — Zaira acciambellata; Bretzel flop sul fianco se rilassato, altrimenti pagnotta a occhi chiusi.
- `CannotReachBowl` — Zaira miagola; Bretzel batte la zampa (thump) e poi si arrende.
- Capacità di movimento: `MaxJumpUp`, `MaxJumpGap`, se può arrampicarsi. Per Bretzel zero: la ricerca
  del percorso trova solo il pavimento su cui sta; un punto irraggiungibile vale come "non ci arriva".
- `ThinkSpecial` — gli stati che ha solo quella specie.

**Stati.** Un solo enum `PetState` (ex `CatState`) con gli stati comuni più quelli di specie, marcati nel
commento: `Meow`, `Hunt`, `Climb` solo gatto; `Groom`, `Binky`, `Flop`, `Thump` solo coniglio. Un enum
chiuso per specie sarebbe più puro ma costringerebbe il gioco e i self-test a conoscere due tipi; il gioco
chiede allo stato solo cose comuni, e ogni cervello ignora quelli che non sono suoi.

**`CatBrain : PetBrain`** — tutto ciò che oggi è solo del gatto: costanti (velocità, salti, caccia,
pagnotta), caccia al cursore, miagolio, arrampicata, prejump/aim, galoppo degli zoomies. Le costanti
restano lì con gli stessi nomi (test e self-test le usano).

**`BunnyBrain : PetBrain`** — vedi sezione 3.

**Gioco.** `Main` crea il cervello dalla `species` del profilo e lavora solo con `PetBrain`. Nomi fissi
"Zaira" (overlay, "Chiama Zaira", tooltip) presi dal profilo. `StateName` passa al cervello (`Describe`).

## 2. Modello e animazioni (Blender)

**Preparazione (`tools/blender/bretzel_prep.py`)** → `work/bretzel.blend`:
- via la sfera estranea (Icosphere, 2 m, senza pesi);
- mesh da 93k a ~15k facce (decimate), texture da 4K a 2K;
- ossa rinominate: `Root`, `Spine0-1`, `Head0-1`, `Nose` (bone_5), `EarL1-4`, `EarR1-4`,
  `ShoulderL/R`, `ArmL1-3`/`ArmR1-3`, `Hips` (bone_22), `HindL1-6`/`HindR1-6`, `Tail` (bone_37);
  bone_23-24 scendono dal bacino fino a terra fra le zampe: da capire cosa muovono (probabilmente
  un errore del rig automatico, da togliere ridando i loro pesi al bacino);
- orientamento come Zaira (muso verso +Z glTF);
- controllo dei pesi con pose estreme (zampa dietro distesa, corpo sdraiato sul fianco, orecchie alzate):
  render da farti vedere; se deforma, correzione dei pesi lì.

**Animazioni (`tools/blender/bretzel_anim.py`)** — costruite a mano con la meccanica del coniglio,
riusando le funzioni generiche di `anim.py` (catene di ossa, piantare le zampe) spostate in un modulo
comune se servono a entrambi:

| Clip | Azione logica | Cosa fa |
|---|---|---|
| Idle | idle | a quattro zampe, respira, orecchie che si muovono appena |
| Hop | hop | saltello lento: anteriori una dopo l'altra, posteriori insieme oltre le anteriori |
| Run | run | corsa a balzi lunghi, schiena che si allunga e raccoglie |
| Binky | binky | salto sul posto con torsione di testa e bacino opposte e calcio a mezz'aria |
| Loaf | loaf | pagnotta, zampe nascoste, respiro |
| Sit | sit | seduto dritto sui posteriori, orecchie un po' alzate |
| Groom | groom | si lava il muso con le anteriori, poi le orecchie |
| Eat | eat | muso giù, mastica |
| Flop / FlopSleep | flop, flopsleep | si butta sul fianco; dorme disteso |
| Thump | thump | batte forte un posteriore, orecchie tese |
| Held | held | penzola, zampe raccolte |
| Fall / Land | fall, land | caduta e atterraggio quando lo lasci |

Ogni clip si guarda in GIF (lato e tre quarti) prima di portarla nell'app, come per Zaira.

## 3. Bretzel nell'app

**`BunnyBrain`:**
- Velocità (a ~110 px di lunghezza, da tarare sulle GIF): saltelli ~70 px/s, corsa ~450 px/s.
- `Decide`: come il gatto ma senza esplorare altre superfici; più tempo a pagnotta e a lavarsi.
- Corse pazze (voglia di giocare alta): corse avanti e indietro sul pavimento; a ogni cambio di
  direzione, a caso, un binky sul posto.
- Sonno: con energia bassa dorme dove si trova. Se è rilassato (contento o accarezzato di recente) prima
  il flop sul fianco poi `flopsleep`; altrimenti pagnotta con `sleep`.
- Ciotola: ci va se è sul pavimento; se è su una finestra e non ci arriva, thump e rinuncia per un po'.
- Coccole: niente fusa; `petted` = occhi socchiusi, si appiattisce un po', a volte digrigna piano
  (nessun suono per ora).
- Niente caccia, arrampicata, salti.

**Orecchie e naso (`src/Game/EarModifier.cs`)**, parente del `TailModifier`: le orecchie seguono il
movimento della testa con ritardo crescente verso la punta (molla smorzata), quindi ballonzolano a ogni
saltello e sventolano nei binky; il naso fremica sempre un poco, più veloce quando si muove o annusa.
Ossa lette da `ear_bones` e `nose_bone` nel profilo; senza, nessun effetto (Zaira non cambia).

## 4. Test

- **Divisione:** tutti i test esistenti passano senza toccarli, a parte il rinomino `CatState` → `PetState`;
  self-test dell'app (`--selftest`, `--selftest-tail`, …) come prima.
- **Salvataggi:** `save.json` esistente diventa quello di Zaira; Bretzel parte dal suo.
- **`BunnyBrainTests`:** non lascia mai il pavimento; mai stati da gatto; negli zoomies compare un binky;
  sonno da rilassato comincia con il flop; ciotola irraggiungibile → thump e poi rinuncia; gait per velocità.
- **App:** self-test del giro con `--cat=bretzel`; screenshot e GIF delle clip.

## Ordine di lavoro

1. Divisione del cervello + salvataggi per animale + nomi dal profilo. Zaira identica. Commit.
2. Preparazione modello Bretzel + render delle pose estreme → te li faccio vedere.
3. Animazioni + GIF → te le faccio vedere.
4. `BunnyBrain`, `EarModifier`, `cats/bretzel/profile.json`, self-test. Commit, poi tu lo provi.

## Fuori da questa versione

Bretzel sulle finestre; Zaira e Bretzel insieme sullo schermo (la divisione lo rende possibile dopo);
suoni.
