# Quick Design Spec: Moduli sovrapponibili

**Type**: Addition
**System**: Moduli dei livelli (LevelBuilder / PoolPhysics / Editor Livelli)
**GDD Reference**: nessun GDD (`workflow: minimal`) — `CLAUDE.md`, sezioni "Formato livelli" e "Moduli sopra il tavolo"
**Date**: 2026-11-10

## Change Summary

Una cella non ha più un solo modulo ma una **pila**: la base (panno, sponda o ponte) e sopra uno o più moduli dal basso verso l'alto. Nell'Editor Livelli un interruttore **"Sovrapposizione"** decide se un modulo dipinto su un altro lo rimpiazza (spento, come oggi) o ci va sopra (acceso).

## Motivation

Combinare i moduli (spine su un muro a tempo, spinta su un blocco, ghiaccio su un cancello) moltiplica i puzzle senza creare un modulo nuovo per ogni combinazione. Nessuna combinazione è scritta a mano: ogni modulo resta sé stesso e interagisce con gli altri in modo naturale.

## Design Delta

Oggi (`CLAUDE.md`, "Moduli sopra il tavolo"):

> i moduli di `OnTopChars` stanno *sopra* la base della cella invece di sostituirla. Nella griglia c'è il modulo; se sotto non c'è panno, la riga `sotto <x> <z> <c>` dice cosa c'è.

Diventa: la cella ha una base e una pila di moduli; ogni modulo poggia sulla cima di quello sotto.

## New Rules / Values

1. **Pila**: ogni cella ha una base e da 0 a `MaxStack` (4) moduli, dal basso verso l'alto.
2. **Solidi e piatti**: i solidi hanno una cima (blocco `P`, cancello `G`, muri a tempo `L` `%`, ventilatore `U` alti 1; respingente `Q` alto 0,5). Tutti gli altri moduli sono piatti (spessore 0).
3. **Appoggio**: il primo modulo poggia sulla superficie della base (panno, cima della sponda, impalcato del ponte). Ogni modulo successivo poggia sulla cima del solido sotto di lui; un piatto sopra un piatto poggia alla stessa altezza.
4. **Un modulo resta sé stesso** (nessuna combinazione scritta a mano): agisce sempre all'altezza a cui si trova in quel momento. I moduli mobili (`G`, `L`, `%`) portano su e giù tutto quello che hanno sopra, grafica, altezza d'azione e pareti dei solidi. Da alzati la cima è +1, da abbassati è a filo dell'appoggio. Le spine su un muro abbassato restano spine, a filo del panno.
5. **Cosa sente la palla**: tutti i moduli piatti che poggiano alla sua altezza (tolleranza `FloorStep / 2`) agiscono insieme. Ghiaccio e spinta sulla stessa cella spingono senza frenare. Un modulo sull'impalcato non tocca chi passa sotto.
6. **Le cime dei solidi sono pavimento**: la palla ci può atterrare e rotolare, e se ne esce cade. Oggi vale solo per i muri mobili alzati; con questa regola vale per ogni solido.
7. **Pareti dei solidi impilati**: vanno dal loro appoggio alla loro cima e si aggiornano quando l'appoggio si muove. Un muro che si alza lancia in alto tutto quello che sta alla sua altezza o sopra.
8. **Esclusi dalla pila**: piattaforma `H` ed estensore `=` (si muovono o lavorano in orizzontale). Una sega `@` impilata sopra un altro modulo non segue gli estensori e gira sul posto.
9. **Formato del file** (compatibile coi livelli esistenti):
   - la griglia contiene il modulo più in basso;
   - `sotto x z c` = base;
   - nuova riga `sopra <x> <z> <c> [gradi]` per ogni modulo sopra il primo, nell'ordine dal basso verso l'alto.
   - Senza righe `sopra` un livello è identico a oggi.
10. **Editor**:
    - interruttore "Sovrapposizione";
    - acceso: un modulo dipinto su una cella con dei moduli va in cima alla pila (se la regola 8 e `MaxStack` lo permettono, altrimenti non succede niente); la Gomma toglie solo il modulo in cima;
    - spento: comportamento di oggi, il modulo nuovo sostituisce la pila;
    - Sposta e Copia/Incolla spostano la pila intera; i pezzi online la portano con sé.

| Valore | Default | Intervallo | Perché |
|---|---|---|---|
| `MaxStack` (moduli per cella) | 4 | 1–6 | evita torri illeggibili |
| Tolleranza dell'altezza (sentire un modulo / atterrare su una cima) | `FloorStep / 2` = 0,25 | 0,1–0,25 | stessa regola del ponte |

## Affected Systems

| System | Impact | Action Required |
|--------|--------|-----------------|
| `LevelBuilder` (formato, `Build`, `ToCells` / `ToText` / `SetTile`) | pila per cella, righe `sopra` | lettura, scrittura e costruzione delle pile |
| `PoolPhysics` (`Modules`, `Cloth`, `GroundAt`, `UpdateMoving`, `Lift`) | più moduli per cella, cime come pavimento, appoggi mobili | ciclo sui moduli piatti alla giusta altezza; appoggi ricalcolati a ogni passo per le pile con un mobile |
| `LevelEditorWindow` | interruttore, gomma, sposta, copia | portare la pila ovunque passa `under` |
| Pezzi online (`AttachPiece`, `PieceCells`) | portano le pile | stesse funzioni di testo |
| Livelli esistenti | regola 6: un salto che finisce su un blocco ora resta sopra invece di scivolare giù | rilanciare il risolutore (`LevelSolverTests`) sui livelli con blocchi e ventole |

## Acceptance Criteria

- [ ] Spine sopra `L`: col muro alzato una palla lanciata in cima muore; col muro abbassato le spine stanno a filo del panno e uccidono chi ci passa; la grafica delle spine sale e scende col muro.
- [ ] Ghiaccio e spinta sulla stessa cella: la palla viene spinta e intanto non frena.
- [ ] Spinta sopra un blocco `P`: una palla che atterra sul blocco viene spinta; una che passa accanto rimbalza sul blocco come prima.
- [ ] Blocco sopra un muro a tempo: le sue pareti salgono e scendono col muro.
- [ ] Il testo salvato con le righe `sopra` si ricarica identico; un livello senza righe `sopra` costruisce lo stesso tavolo di oggi.
- [ ] Editor: con la sovrapposizione accesa la Gomma toglie solo il modulo in cima; con la sovrapposizione spenta dipingere sostituisce come oggi.
- [ ] No regression: `PhysicsTests`, `ModuleTests`, `BuildPhaseTests`, `RulesTests` verdi.

## GDD Update Required?

No (non esiste un GDD). Va aggiornata la sezione "Formato livelli" di `CLAUDE.md` (righe `sopra`, pile, cime come pavimento).
