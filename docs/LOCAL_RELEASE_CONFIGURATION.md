# Local release configuration

Keep the actual checkout, deployment and library paths off GitHub. In the
maintained source checkout, open PowerShell and confirm that the current
directory is the checkout you intend to build or publish from. Then run:

```powershell
Set-Content -LiteralPath .pixelvault-release-root -Value (Get-Location).Path
git check-ignore .pixelvault-release-root
```

The file contains one absolute path, is ignored by Git and should not be copied
into the older runtime checkout. Alternatively, set
`PIXELVAULT_MAINTAINED_SOURCE` to the absolute maintained checkout for the build
process. The environment variable takes precedence. The release guard will
still stop when configuration is missing, malformed, or points at a different
checkout. This replaces the absolute pointer formerly kept in
`docs/CURRENT_BUILD.txt`; published packages, running services and user data
are not changed by this migration. In published docs, replace
`<source-root>` and `<runtime-root>` with the corresponding local paths before
using example commands.

For a trusted home-LAN Compose deployment, set `COMPANION_LAN_IP` to the
actual host bind address in the ignored `deploy/companion/.env` (or in the
Compose environment). If omitted, the home-LAN template binds to loopback
only. Preserve the former bind address there **before the next Compose
recreation** if existing LAN clients should continue to reach the service.

For QNAP, supply the actual host as `COMPANION_ALLOWED_HOST`, or replace the
documented example host in the Container Station environment when copying the
template. Before recreating an existing deployment, keep its current host
allowlist and NAS-local volume settings in a private override or the local
Container Station configuration. See [Companion NAS setup](COMPANION_NAS.md).
Do not commit a resolved Compose configuration, local installation receipts,
or screenshots showing host details.
