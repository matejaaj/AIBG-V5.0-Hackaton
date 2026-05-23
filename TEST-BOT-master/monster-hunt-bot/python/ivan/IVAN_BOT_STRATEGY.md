# Ivan Bot — Strategy Documentation

## Praćenje stanja protivnika

Bot svaki potez prati sledeće informacije o protivniku:
- Trenutnu poziciju na mapi
- Trenutni HP
- **Inventar** (koje predmete nosi)
- **Aktivne statusne efekte** (Frozen, Dizzy itd.)

Sve ovo se loguje u JSON formatu pri svakom potezu.

---

## Prioritet sakupljanja predmeta

Na početku igre prioritet **nije** healing — bot preferira CC scrollove:

| Prioritet | Predmet |
|-----------|---------|
| 🥇 1 | Freeze Scroll |
| 🥈 2 | Confusion Scroll |
| 🥉 3 | Monster Card |
| 4 | Healing Potion (samo ako je HP nizak, < 55%) |
| 5 | Healing Potion (ako je HP uredu — niska prioriteta) |

---

## Kretanje: Snow vs Ice Spikes

- Bot preferira prolaz kroz **snow tile** (`OBSTACLE_SLOW`) umesto ice spike tilea (`OBSTACLE`).
- Snow tile je uvek prohodni (košta +1 movement ali je bezbedan).
- **Ice spike tile** je prohodni **samo ako bot nosi Healing Potion** u inventaru (10 DMG se isplati jer ima heal).

---

## Takmičenje za pickup (Contest Pickup)

Ako i protivnik i bot idu ka istom **CC scrollu ili monster card-u**:

1. Proveriti da li je protivnik jednako blizu ili bliže tom predmetu.
2. Ako da i ima Freeze Scroll → **upotrebi Freeze Scroll** na protivnika.
3. Ako nema Freeze → upotrebi **Confusion Scroll**.
4. Nakon CC-ja → sprint do predmeta i pokupiti ga pre protivnika.

---

## Freeze + Attack Combo

Kada je protivnik dovoljno blizu (u opsegu: `maxMove + attackRange + 2`):

### Faza 1 — protivnik slobodan:
- Upotrebi **Freeze Scroll** → protivnik ne može da se kreće 2 poteza.
- *Napomena: ako ima Ice Cube card, `ice_cube_freeze` combo preuzima prioritet.*

### Faza 2 — protivnik zamrznut:
- Priđi što bliže (protivnik ne može pobeći).
- **Udari ga** svaki potez dok traje Frozen status.

---

## Ice Cube + Freeze Combo

Kada bot ima i **Ice Cube card** i **Freeze Scroll**, a protivnik je u blizini:

1. **Potez 1** — Upotrebi Freeze Scroll → protivnik zamrznut.
2. **Potez 2** — Prizovi Ice Cube **pored zamrznutog protivnika**.
   - Ice Cube automatski napada tokom `MonsterTurn`.
3. **Potez 3+** — Bot i dalje napada zamrznutog protivnika.
   - Ako ima još jedan Freeze Scroll i Ice Cube još ima poteza → ponovi freeze.

> Ice Cube stats: 30 HP, 20 DMG, 3 tile movement, 5 turn cooldown

---

## Zone Trap — Skupljanje mape

Mapa se **skuplja svakih 15 poteza**. Prve kolone koje nestaju su:
- **Leva strana**: `X = 0, 1, 2`
- **Desna strana**: `X = 29, 30, 31`

### Logika:

```
Ako je protivnik na X <= 2 ili X >= 29
  I skupljanje mape je za <= 2 poteza:
    Ako ima Freeze Scroll → upotrebi Freeze (ne može da pobegne)
    Inače ako ima Confusion Scroll → upotrebi Confusion
```

- **Freeze** je bolji jer protivnik ne može ni da se pomeri.
- **Confusion** je fallback ako nema Freeze.
- Ako je protivnik već CC'd — ne troši scroll, čekaj da umre.

---

## Redosled prioriteta (kompletna lista)

| # | Akcija | Opis |
|---|--------|------|
| 1 | **Lethal** | Ubij protivnika ako možeš jednim udarcem |
| 2 | **Emergency** | Isleči se / beži ako si ispod 30% HP |
| 3 | **Zone Trap** | Zamrzni protivnika u zoni koja se ruši (vremenski osetljivo!) |
| 4 | **Edge Safety** | Pobegni sa terrajna koji nestaje |
| 5 | **Ice Cube + Freeze** | Zamrzni → prizovi Ice Cube pored protivnika |
| 6 | **Freeze Combo** | Zamrzni kad si blizu → priđi → udari |
| 7 | **Opportunity** | Udari ako si već u dometu |
| 8 | **Contest Pickup** | CC protivnika koji trči ka istom predmetu |
| 9 | **Pickup** | Pokupiti predmete (CC > Cards > Heal) |
| 10 | **Summon** | Prizovi monster card (po mogućnosti pored protivnika) |
| 11 | **Powerup** | Idi ka najboljoj POWERUP pločici |
| 12 | **Pressure** | Juri protivnika / gurni ga prema ivici kasno u igri |
| 13 | **Fallback** | Idi ka centru / bilo koji slobodan korak |
