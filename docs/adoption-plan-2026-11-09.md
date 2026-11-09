# Adoption Plan

> **Generated**: 2026-11-09
> **Project phase**: Production (dedotta, nessuno `project.stage`)
> **Engine**: Unity 6000.5.2f1 (installato; non ancora in project.yaml)
> **Template version**: v1.0+

Work through these steps in order. Check off each item as you complete it.
Re-run `/adopt` anytime to check remaining gaps.

---

## Step 1: Fix Blocking Gaps

### 1.1 `project.yaml` illeggibile: nessun interprete Python
`yaml-helper` non trova `python`, `python3` né `py`, quindi tutte le skill usano i default.
Fix: installare Python 3 (aggiungerlo al PATH) e rilanciare una skill per verificare che l'avviso sparisca. In alternativa, accettare i default (workflow `minimal`, review `solo`).
**Time**: 5–15 min
- [x] yaml-helper legge project.yaml senza errori

---

## Step 2: Fix High-Priority Gaps

### 2.1 Configurare l'engine (`/setup-engine unity 6000.5`)
Mancano il blocco `engine`, `specialists`, `naming`, `commands` in `project.yaml` e i valori in `technical-preferences.md`.
Il riepilogo di scrittura è già stato presentato, serve solo il tuo "ok". Scelte già fatte:
- build Windows 64-bit
- PC solo per ora, telefono in futuro
- mouse e tastiera, gamepad Partial, touch None
- budget prestazioni lasciati `[TO BE CONFIGURED]`
- ponte test via editor aperto (`Assets/Editor/TestBridge.cs`), niente batchmode
**Time**: 30 min
- [x] engine.* in project.yaml
- [x] technical-preferences.md compilato

### 2.2 Aggiornare `docs/engine-reference/unity/VERSION.md` da 6.3 LTS a 6000.5.2f1
Fa parte di `/setup-engine` (sezione 7): riga "Installed at pin time", sezione `6.3 → 6.5` nei file breaking-changes / deprecated-apis / current-best-practices, solo con fatti presi dalle pagine ufficiali.
**Time**: 30 min
- [x] VERSION.md pinna 6000.5.2f1
- [x] sezioni `6.3 → 6.5` appese

### 2.3 Creare `design/game-brief.md`
A `minimal` è l'unico documento di design. Il progetto esiste già, quindi `/reverse-document` lo ricava dal codice e da CLAUDE.md. Alternativa: `/brainstorm`.
Campi richiesti: pitch, core loop, player goal e fail state, MVP, out of scope, build order. Il goal e il fail state sono già in CLAUDE.md (nera in buca, restart).
**Time**: 30 min
- [ ] design/game-brief.md con i sei campi

---

## Step 3: Bootstrap Infrastructure

Non è sul percorso `minimal`: il build order del brief è il piano. Prossimo passo: `/create-stories` (o `/dev-story` quando le storie esistono).

---

## Step 4: Medium-Priority Gaps

- [x] `naming.*` configurati (coperto da 2.1)
- [ ] `performance.*`: fissare i budget quando il target telefono si concretizza (`/settings`)

---

## Step 5: Optional Improvements

- [ ] Forbidden Patterns / Allowed Libraries: popolare quando servono

---

## What to Expect from Existing Stories

Non ci sono storie. Le future nascono già nel formato giusto.

---

## Re-run

Run `/adopt` again after completing Step 2 to verify all blocking and high gaps are resolved.
