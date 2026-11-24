# Architecture drawings

Each drawing makes one claim. Solid arrows show synchronous requests or local updates. Dashed arrows carry published messages. The reservation drawing outlines the winning transaction. Each SVG includes a title, a text alternative and stable group IDs for the named nodes and edges. The overview's PostgreSQL row names the schema owned by each module.

| Drawing | Text alternative | Compact view | Share image |
| --- | --- | --- | --- |
| [System overview](module-map.svg) | Lending asks Catalog for descriptions and sends hold facts to PickupDesk. One host contains the three modules. Their roles use three separately owned PostgreSQL schemas. | [Light](exports/module-map/mobile-light.svg) · [Dark](exports/module-map/mobile-dark.svg) | [Light PNG](exports/module-map/light.png) · [Dark PNG](exports/module-map/dark.png) |
| [Reservation](reserve-sequence.svg) | Two members want one copy. A conditional write, a hold and its outgoing fact commit in one transaction. A competing request sees the conflict. | [Light](exports/reserve-sequence/mobile-light.svg) · [Dark](exports/reserve-sequence/mobile-dark.svg) | [Light PNG](exports/reserve-sequence/light.png) · [Dark PNG](exports/reserve-sequence/dark.png) |
| [Repeated delivery](delivery-recovery.svg) | The relay retries the original message ID. The inbox durably accepts a receipt. Another transaction applies the newest snapshot and completes the receipt. | [Light](exports/delivery-recovery/mobile-light.svg) · [Dark](exports/delivery-recovery/mobile-dark.svg) | [Light PNG](exports/delivery-recovery/light.png) · [Dark PNG](exports/delivery-recovery/dark.png) |
| [Deployment](deployment.svg) | The API handles HTTP and runs the relay and inbox worker. Its three data sources connect to one PostgreSQL instance. Migrations complete before startup. | [Light](exports/deployment/mobile-light.svg) · [Dark](exports/deployment/mobile-dark.svg) | [Light PNG](exports/deployment/light.png) · [Dark PNG](exports/deployment/dark.png) |

The [scene files](design) own the wording, direction and stable IDs. The [layout](layout.json) specifies the card, boundary and footer geometry for 1000-pixel and 360-pixel canvases. The wide SVGs adapt to the reader's color scheme. The [fixed-theme exports](exports) work in presentations and image viewers. Share PNGs are 1600 × 688 pixels.

## Rebuild the figures

From the repository root, use Python 3.9 or later and Node 20 or later.

```sh
python3 tools/reference/render_diagrams.py
npm ci --prefix tools/reference/raster --ignore-scripts
npm --prefix tools/reference/raster run render
python3 tools/reference/render_diagrams.py --check
npm --prefix tools/reference/raster run check
```

The PNG renderer uses a locked WebAssembly build and the [included Barlow faces and license](../../tools/reference/raster/fonts/README.md) so macOS and Linux can produce the same bytes. Keep all variants in one change when updating a drawing. The manifest uses the adaptive SVG. Each alternate asset remains accessible at the same Git revision.
