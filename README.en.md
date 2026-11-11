# PadesSigner

A simple Windows tool that runs in the background, watches an input folder and signs every PDF you put there
with a **PAdES** electronic signature, then saves it to an output folder.

[Slovenská verzia](README.md)

```
Input\invoice.pdf ──sign──► Output\invoice_signed.pdf
        │
        ├─ success ─► Processed\invoice.pdf (original)
        └─ failure ─► Error\invoice.pdf + invoice.pdf.error.txt
```

- No GUI, just an icon among the system notification icons (it may be hidden in the tray under the **^** arrow).
- Signs with a certificate from the **Windows certificate store**: no key file or password needs to be stored on disk.
  Works with a smart card or USB token too.
- Signing with PFX and PEM files is also possible.
- The signature is invisible (no stamp on the page). Existing digital signatures in the PDF stay valid.

## Requirements

- Windows 10 or 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (not needed for the *self-contained* build)
- a signing certificate with a private key, installed in Windows, or pfx/pem certificate files

## Installation

### 1a. Build the program

Command line + dotnet:

```bash
dotnet publish PadesSigner -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

### 1b. Or download the built program

Releases here on GitHub -> unzip

---------

Result: `PadesSigner.exe`, `appsettings.json` and `CERTIFICATE-SETUP.txt`.

### 2. Location

Copy these three files to any folder you have write access to.
`appsettings.json` must always sit next to `PadesSigner.exe`.

### 3. Certificate

1. If you don't have one yet, install the certificate: double-click the `.pfx` file → location **Current User** → enter the password.
   Do **not mark the key as exportable** (it is safer and the program doesn't need it).
   For a smart card / token, just plug it in.
2. If you have **a single** signing certificate, the program finds it by itself. Nothing to configure.
3. If you have **several**, after starting the program right-click the icon → **Choose certificate…** and pick the preferred one.
   The choice is saved to `appsettings.json`.

Detailed instructions (including finding the thumbprint by hand via `certmgr.msc`, if needed) are in [`CERTIFICATE-SETUP.txt`](PadesSigner/CERTIFICATE-SETUP.txt).

### 4. Start

Run `PadesSigner.exe`. A Windows shield icon appears among the system icons.

Tip: start automatically at every sign-in: right-click the icon → **Start with Windows**
(or from cmd `PadesSigner.exe --install`; to turn it off, `PadesSigner.exe --uninstall`).

## Usage

1. Put a PDF into `%USERPROFILE%\PadesSigner\Input` (or icon menu → **Open input folder**).
2. Shortly afterwards the signed file appears in `Output` as `name_signed.pdf`.
3. The original is moved to `Processed`. If signing fails, the original goes to `Error` together with a `.error.txt` file describing the problem.

Files that are already in the input folder at startup are signed too. A file is processed only once another program
has finished writing it (or, for example, once copying from another drive is complete).

### Icon menu (right-click)

| Item | Meaning |
|---|---|
| status | *Running…* = signing, *Not signing…* = not signing + reason; number of signed files |
| Open input / output / error folder | opens the folder |
| Choose certificate… | choose the signing certificate |
| Edit settings | opens `appsettings.json` in Notepad |
| Reload settings | reloads the settings (also happens automatically when the file is saved) |
| Open log | opens the log |
| Start with Windows | turns automatic start on / off |
| Exit | exits the program |

**Shield** icon = the program is signing. **Warning** icon = not signing (e.g. no certificate); hover over the icon
to see why. Files wait in the input folder meanwhile and are signed as soon as the problem is fixed.

## Settings (`appsettings.json`)

To edit: icon menu → **Edit settings**, save. The program reloads by itself. Comments (`//`) are allowed.
Paths may contain environment variables (`%USERPROFILE%`) or be relative to the exe.

| Key | Default | Meaning |
|---|---|---|
| `InputFolder` | `%USERPROFILE%\PadesSigner\Input` | watched folder |
| `OutputFolder` | `…\Output` | signed files |
| `ProcessedFolder` | `…\Processed` | originals after signing; empty value = the original is deleted |
| `ErrorFolder` | `…\Error` | files that could not be signed |
| `OutputSuffix` | `_signed` | suffix of the signed file name; may be empty |
| `RescanIntervalSeconds` | `30` | how often the input folder is re-checked for changes |
| `Certificate.StoreThumbprint` | empty | certificate thumbprint; empty = automatic choice |
| `Certificate.StoreLocation` | `CurrentUser` | `CurrentUser` or `LocalMachine` |
| `Signature.Level` | `B-B` | signature level, see below |
| `Signature.TsaUrl` | empty | timestamp server (TSA) address |
| `Signature.TsaUser`, `TsaPassword` | empty | TSA credentials, if it requires them |
| `Signature.DigestAlgorithm` | `SHA-256` | `SHA-256`, `SHA-384` or `SHA-512` |
| `Signature.Reason`, `Location`, `Contact` | empty | optional details shown in the signature (reason, location, contact) |

Instead of the Windows store you can also use a `.pfx` or `.pem` file (`Certificate.Source` = `Pfx` / `Pem`),
see the comments in `appsettings.json`.

### Signature level

| Level | What it adds | Needs |
|---|---|---|
| `B-B` | basic signature; the signing time comes **from your PC's clock** | nothing |
| `B-T` | + **timestamp** from a trusted server (proves the signing time) | `TsaUrl`, internet |
| `B-LT` | + certificate validity data (OCSP/CRL) embedded in the PDF, verifiable even years later | `TsaUrl`, internet, a certificate with an OCSP/CRL address |
| `B-LTA` | + timestamp over the whole document (long-term archiving) | same as `B-LT` |

Recommendation: **`B-T`** for everyday use, `B-LT` / `B-LTA` for documents that are archived.
For a qualified signature under eIDAS, use a **qualified TSA** (e.g. from your certificate provider).

## Troubleshooting

**Adobe Reader shows a "signature validity is unknown" warning**
The signature is fine (what matters is *the document has not been modified since it was signed*); Adobe just doesn't know the certificate's issuer.
Adobe has its own list of trusted authorities (AATL, EUTL) and doesn't look at the Windows store. Options:

- a certificate from a qualified provider (EUTL) is trusted automatically;
- for a company certificate authority that Windows trusts: *Edit → Preferences → Signatures → Verification → More…*
  → *Windows Integration* → tick *Validating signatures*;
- or in the signature panel: *Certificate Details → Trust → Add to Trusted Certificates*.

This is a setting on the side of **whoever verifies the signature**, not of this program.

**"Signing time is from the clock on the signer's computer"**
At level `B-B` the time is not verified by anything. Set `Level` to `B-T` and set `TsaUrl` (see above).

**Warning icon / files stay in the input folder**
Hover over the icon or open **Open log**. Most common causes: no certificate found, several certificates
(→ **Choose certificate…**), the certificate has expired, or `TsaUrl` is missing for `B-T`.

**A file ended up in `Error`**
The reason is in `name.pdf.error.txt` next to it. After fixing it, move the file back to `Input`.

**Starting it a second time does nothing**
The program is already running (only one instance can run). Look for the icon near the clock, including under the **^** arrow.

**Smart card / token**
On the first signature, Windows or the card driver asks for the PIN.

**Log**
`%LOCALAPPDATA%\PadesSigner\logs\PadesSigner-YYYY-MM-DD.log`

## License

The program uses the [iText](https://itextpdf.com) (AGPL-3.0) and [BouncyCastle](https://www.bouncycastle.org) (MIT) libraries.
Distributing the program is therefore subject to the **AGPL-3.0** terms, including making the source code available.
