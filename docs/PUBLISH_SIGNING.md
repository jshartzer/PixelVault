# Authenticode signing — publish ritual (**PV-PLN-DIST-001** §5.2)

PixelVault can **Authenticode-sign** release binaries so Windows users see a **known publisher** instead of “Unknown publisher” on every launch (SmartScreen reputation still varies by cert type and reputation).

This doc is the **one-machine ritual**. **Legal / procurement** (buying an OV or EV cert, token storage) stays outside the repo.

---

## Prerequisites

- **Code-signing certificate** installed in **Windows certificate store** (**Current User** or **Local Machine** → **Personal**). Note the certificate **thumbprint** (40 hex chars, no spaces in env form optional).
- **signtool.exe** from **Windows SDK** / Windows Kits — typically under  
  `%ProgramFiles(x86)%\Windows Kits\10\bin\<build>\x64\signtool.exe`.  
  **`Publish-PixelVault.ps1`** searches for **`x64\signtool.exe`** automatically, or pass **`-SignToolPath`**.

---

## Zip-style **`dist/PixelVault-*`** (`Publish-PixelVault.ps1`)

Signs **`PixelVault.exe`** after **`dotnet publish`** (single-file layout).

```powershell
# Thumbprint from certmgr / MMC, or export from your CA workflow:
$env:PIXELVAULT_AUTHENTICODE_THUMBPRINT = "YOUR40CHARHEXTHUMBPRINTHERE"
pwsh -File C:\Codex\scripts\Publish-PixelVault.ps1 -Force -Sign
```

Or pass **`-CertificateThumbprint`** explicitly. Optional **`-TimestampServer`** (default matches DigiCert HTTP timestamp used in script).

**Note:** Only the **main EXE** is signed by this script today — not companion DLLs in other layouts. Adjust if you add multi-file signing.

---

## Velopack / **`vpk pack`** (`Publish-Velopack.ps1`)

**`Publish-Velopack.ps1`** does **not** pass signing flags to **`vpk`** yet. After publish, **`vpk pack`** may warn that files are unsigned.

Use **`vpk pack`** **`-n` / `--signParams`** so Velopack invokes **signtool** with the same thumbprint/timestamp style:

```powershell
# Example: append to the pack step (thumbprint + timestamp — align with your CA)
vpk pack `
  -u Codex.PixelVault `
  -v 0.76.0 `
  -p "C:\Codex\dist\Velopack\publish-0.076.000" `
  -e PixelVault.exe `
  -o "C:\Codex\dist\Velopack\0.76.0" `
  -n "/fd SHA256 /sha1 YOUR40CHARHEXTHUMBPRINT /tr http://timestamp.digicert.com /td SHA256"
```

Velopack also documents **`VPK_SIGN_PARAMS`** and **`--signTemplate`** for custom signers — see **[Velopack CLI](https://docs.velopack.io/reference/cli)**.

Use **`--signSkipDll`** if you only want EXEs signed during pack.

---

## Verification

- Right-click **`PixelVault.exe`** (or setup) → **Properties** → **Digital Signatures** tab should list your certificate.
- On a clean VM, first-run SmartScreen behavior depends on **EV/OV**, **reputation**, and **Microsoft Defender SmartScreen** — signing is necessary but not sufficient for instant trust.

---

## Related

- **`scripts/Publish-PixelVault.ps1`** — **`-Sign`** implementation.
- **`docs/VELOPACK.md`** — installer channel.
- **`docs/PRIVACY_POLICY.md`** — listing often wants signed binaries **and** a hosted privacy URL (**§5.4**).
