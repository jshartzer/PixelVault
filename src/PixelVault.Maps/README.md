# PixelVault saved maps

Platform-neutral models, validation and read-only queries for the existing `game-maps/maps.sqlite` store. Native and Companion reference this project. The original namespace and JSON names are retained so existing definitions need no conversion.

`GameMapReader` opens SQLite with `Mode=ReadOnly`, without pooling, schema initialization or migrations. `List` scopes by exact provider/game and optional achievement/tracker links; `Find` additionally scopes by map ID. The SQL row's revision is authoritative. Definitions are checked against row identities and normalized pin bounds before they are returned.

By default a missing store returns an empty list for first-time desktop browsing. Companion uses `allowMissing: false` after checking whether a map directory exists, so an unavailable saved store is not silently replaced or reported empty. Filesystem redirects in the map directory/database/WAL/SHM are rejected.

Native retains image import/copying and optimistic writes in `Services/Achievements/GameMaps.cs`. This project does not decode images, read tracker progress or execute save readers. Companion exposes its own DTOs instead of serializing internal definitions to clients.
