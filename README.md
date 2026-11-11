# PadesSigner

Jednoduchý nástroj pre Windows, ktorý beží na pozadí, sleduje vstupný priečinok a každé PDF, ktoré doň vložíte,
podpíše elektronickým podpisom **PAdES** a uloží do výstupného priečinka.

[English version](README.en.md)

```
Input\faktura.pdf ──podpis──► Output\faktura_signed.pdf
        │
        ├─ úspech ─► Processed\faktura.pdf (originál)
        └─ chyba  ─► Error\faktura.pdf + faktura.pdf.error.txt
```

- Bez GUI, iba ikona medzi systémovými oznámeniami/ikonami (môže byť skrytá v tray pod šípkou **^**).
- Podpisuje certifikátom z **úložiska certifikátov Windows**: na disku nie je potrebný uložený žiadny súbor s kľúčom ani heslo.
  Funguje aj s čipovou kartou alebo USB tokenom.
- Možnosť certifikovať aj so súbormi PFX a PEM 
- Podpis je neviditeľný (bez pečiatky na strane). Existujúce digitálne podpisy v PDF zostávajú platné.

## Požiadavky

- Windows 10 alebo 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (netreba pri verzii *self-contained*)
- podpisový certifikát s privátnym kľúčom, nainštalovaný vo Windows alebo certifikáty pfx/pem

## Inštalácia

### 1a. Zostavenie programu

Príkazový riadok + dotnet:

```bash
dotnet publish PadesSigner -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

### 1b. Alebo stiahnutie zostaveného programu

Release tu na github -> unzip 
Len exe verzia - 11MB po rozbalení
Self contained verzia -  má všetky frameworky/libs zabalené , netreba nič inštalovať navyše , ale po rozbalení asi 130MB

---------

Výsledok: `PadesSigner.exe`, `appsettings.json` a `CERTIFICATE-SETUP.txt`.

### 2. Umiestnenie

Skopírujte tieto tri súbory do ľubovoľného priečinka, kam máte právo zápisu.
`appsettings.json` musí byť vždy vedľa `PadesSigner.exe`.

### 3. Certifikát

1. Ak nemáte - nainštalujte certifikát: dvojklik na súbor `.pfx` → umiestnenie **Aktuálny používateľ** → zadajte heslo.
   Kľúč **neoznačujte ako exportovateľný** (je to bezpečnejšie a programu to neprekáža).
   Pri čipovej karte / tokene ho stačí pripojiť.
2. Ak máte **jediný** podpisový certifikát, program ho nájde sám. Nič netreba nastavovať.
3. Ak ich máte **viac**, po spustení kliknite pravým tlačidlom na ikonu → **Choose certificate…** a vyberte preferovaný.
   Výber sa uloží do `appsettings.json`.

Podrobný návod (aj prípadné ručné zistenie odtlačku cez `certmgr.msc`) je v [`CERTIFICATE-SETUP.txt`](PadesSigner/CERTIFICATE-SETUP.txt).

### 4. Spustenie

Spusti `PadesSigner.exe`. Medzi systémovými ikonami sa objaví ikona win-štítu.

Tip: Automatické spúšťanie po každom prihlásení: pravý klik na ikonu → **Start with Windows**
(alebo cez cmd `PadesSigner.exe --install`, prípadné vypnutie `PadesSigner.exe --uninstall`).

## Používanie

1. Vložte PDF do priečinka `%USERPROFILE%\PadesSigner\Input` (alebo menu ikony → **Open input folder**).
2. O chvíľu sa podpísaný súbor objaví v `Output` ako `nazov_signed.pdf`.
3. Originál sa presunie do `Processed`. Ak sa podpis nepodarí, originál je v `Error` spolu so súborom `.error.txt` s popisom chyby

Súbory, ktoré už sú vo vstupnom priečinku pri spustení, sa podpíšu tiež. Súbor sa spracuje až keď ho iný program
dopíše (alebo sa napríklad dokončí prenos súborov z iného disku...).

### Menu ikony (pravý klik)

| Položka | Význam |
|---|---|
| stav | *Running…* = podpisuje, *Not signing…* = nepodpisuje + dôvod; počet podpísaných súborov |
| Open input / output / error folder | otvorí príslušný priečinok |
| Choose certificate… | výber podpisového certifikátu |
| Edit settings | otvorí `appsettings.json` v Poznámkovom bloku |
| Reload settings | znovu načíta nastavenia (deje sa aj automaticky po uložení súboru) |
| Open log | otvorí denník (log) |
| Start with Windows | zapne / vypne automatické spúšťanie |
| Exit | ukončí program |

Ikona **štítu** = program podpisuje. Ikona **výstrahy** = nepodpisuje (napr. chýba certifikát), dôvod zobrazí
prejdenie myšou nad ikonou. Súbory medzitým čakajú vo vstupnom priečinku a podpíšu sa hneď po oprave.

## Nastavenia (`appsettings.json`)

Úprava: menu ikony → **Edit settings**, uložiť. Program sa prenačíta sám. Komentáre (`//`) sú povolené.
Cesty môžu obsahovať premenné prostredia (`%USERPROFILE%`) alebo byť relatívne k exe.

| Kľúč | Predvolené | Význam |
|---|---|---|
| `InputFolder` | `%USERPROFILE%\PadesSigner\Input` | sledovaný priečinok |
| `OutputFolder` | `…\Output` | podpísané súbory |
| `ProcessedFolder` | `…\Processed` | originály po podpise; prázdna hodnota = originál sa vymaže |
| `ErrorFolder` | `…\Error` | súbory, ktoré sa nepodarilo podpísať |
| `OutputSuffix` | `_signed` | prípona názvu podpísaného súboru; môže byť prázdna |
| `RescanIntervalSeconds` | `30` | ako často sa vstupný priečinok kontroluje |
| `Certificate.StoreThumbprint` | prázdne | odtlačok certifikátu; prázdne = automatický výber |
| `Certificate.StoreLocation` | `CurrentUser` | `CurrentUser` alebo `LocalMachine` |
| `Signature.Level` | `B-B` | úroveň podpisu, pozri nižšie |
| `Signature.TsaUrl` | prázdne | adresa servera časovej pečiatky (TSA) |
| `Signature.TsaUser`, `TsaPassword` | prázdne | prihlasovacie údaje k TSA, ak ich vyžaduje |
| `Signature.DigestAlgorithm` | `SHA-256` | `SHA-256`, `SHA-384` alebo `SHA-512` |
| `Signature.Reason`, `Location`, `Contact` | prázdne | voliteľné údaje zobrazené v podpise (dôvod, miesto, kontakt) |

Namiesto úložiska Windows je možné použiť aj súbor `.pfx` alebo `.pem` (`Certificate.Source` = `Pfx` / `Pem`),
pozri komentáre v `appsettings.json`.

### Úroveň podpisu

| Úroveň | Čo pridáva | Potrebuje |
|---|---|---|
| `B-B` | základný podpis; čas podpisu je **z hodín vášho PC** | nič |
| `B-T` | + **časová pečiatka** z dôveryhodného servera (dokazuje čas podpisu) | `TsaUrl`, internet |
| `B-LT` | + údaje o platnosti certifikátu (OCSP/CRL) vložené do PDF, overiteľné aj po rokoch | `TsaUrl`, internet, certifikát s OCSP/CRL adresou |
| `B-LTA` | + časová pečiatka celého dokumentu (dlhodobá archivácia) | ako `B-LT` |

Odporúčanie: **`B-T`** pre bežné použitie, `B-LT` / `B-LTA` pre dokumenty, ktoré sa archivujú.
Pre kvalifikovaný podpis podľa eIDAS použite **kvalifikovanú TSA** (napr. od poskytovateľa vášho certifikátu).

## Riešenie problémov

**Adobe Reader zobrazuje výstrahu „platnosť podpisu je neznáma“**
Podpis je v poriadku (dôležité je *dokument nebol od podpisu zmenený*), Adobe len nepozná vydavateľa certifikátu.
Adobe má vlastný zoznam dôveryhodných autorít (AATL, EUTL) a nepozerá sa na úložisko Windows. Riešenia:

- certifikát od kvalifikovaného poskytovateľa (EUTL) je dôveryhodný automaticky;
- pri firemnej certifikačnej autorite, ktorej dôveruje Windows: *Edit → Preferences → Signatures → Verification → More…*
  → *Windows Integration* → zaškrtnite *Validating signatures*;
- alebo v paneli podpisov: *Certificate Details → Trust → Add to Trusted Certificates*.

To je nastavenie na strane toho, **kto podpis overuje**, nie tohto programu.

**„Čas podpisu je z hodín počítača podpisovateľa“**
Pri úrovni `B-B` nie je čas ničím overený. Nastavte `Level` na `B-T` a `TsaUrl` (pozri vyššie).

**Ikona výstrahy / súbory zostávajú vo vstupnom priečinku**
Prejdite myšou nad ikonu alebo otvorte **Open log**. Najčastejšie: nenašiel sa certifikát, je ich viac
(→ **Choose certificate…**), certifikát vypršal alebo pri `B-T` chýba `TsaUrl`.

**Súbor skončil v `Error`**
Dôvod je v `nazov.pdf.error.txt` vedľa neho. Po oprave ho presuňte späť do `Input`.

**Druhé spustenie nič neurobí**
Program už beží (môže bežať iba jedna inštancia). Hľadajte ikonu pri hodinách, aj pod šípkou **^**.

**Čipová karta / token**
Pri prvom podpise sa zobrazí výzva na PIN od Windows alebo ovládača karty.

**Denník (log)**
`%LOCALAPPDATA%\PadesSigner\logs\PadesSigner-RRRR-MM-DD.log`

## Licencia

Program používa knižnicu [iText](https://itextpdf.com) (AGPL-3.0) a [BouncyCastle](https://www.bouncycastle.org) (MIT).
Pri šírení programu sa preto vzťahujú podmienky **AGPL-3.0**, vrátane sprístupnenia zdrojového kódu.
