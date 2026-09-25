# Da fare

Note lasciate il 25/09 a fine giornata.

## Unire `feat/sleep-curled` in `main`

Il ramo (worktree `C:\develop\GIT\_wt\ZairaDesktopPet\sleep-curl`) contiene la posa del sonno acciambellata.
È rimasto separato perché un'altra sessione aveva modifiche non committate a `tools/blender/anim.py` e
`cats/zaira/zaira.glb` in `main`.

1. Quando quelle modifiche sono committate: `git merge feat/sleep-curled` in `main`.
2. `anim.py` si unisce da solo (modifiche in punti diversi); `zaira.glb` andrà in conflitto: **non** scegliere
   una delle due versioni, rigenerarlo dall'`anim.py` unito (`anim.py` poi `export.py`, vedi
   `tools/blender/README.md`).
3. Rilanciare l'autotest del giro (`-- --selftest`): deve dormire acciambellata col muso verso chi guarda.
4. Rimuovere il worktree e il ramo.

## Piccole cose

- La "z" sopra il gatto che dorme è troppo in alto: l'altezza viene dalla taglia del gatto in piedi
  (`SizePx.Y` in `Main`), la posa acciambellata è molto più bassa.
- Nel giro il controllo "mangia con il muso sopra la ciotola" a volte non scatta: scatta solo se mangia per
  più di 0,8 s, e con "restless" la fame va a 0,1 subito dopo. Si potrebbe aspettare `Eat` con `Until` come
  per zoomies e sonno.
- La pipeline Blender ha percorsi fissi (`_assets/zaira/work/anim.blend`, e `export.py` scrive nel `zaira.glb`
  di `C:\develop\personal\ZairaDesktopPet`): da un worktree va lanciata con i percorsi di uscita sostituiti,
  o renderli parametri.
