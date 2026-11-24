import { readFile, readdir, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { initWasm, Resvg } from "@resvg/resvg-wasm";

const require = createRequire(import.meta.url);
await initWasm(await readFile(require.resolve("@resvg/resvg-wasm/index_bg.wasm")));

const exportsRoot = new URL("../../../docs/diagrams/exports/", import.meta.url);
const fonts = await Promise.all(["Regular", "Bold"].map(weight =>
  readFile(new URL(`fonts/Barlow-${weight}.ttf`, import.meta.url))
));
const check = process.argv.includes("--check");
for (const directory of (await readdir(exportsRoot, { withFileTypes: true })).filter(entry => entry.isDirectory())) {
  for (const theme of ["light", "dark"]) {
    const base = new URL(`${directory.name}/`, exportsRoot);
    const svg = await readFile(new URL(`${theme}.svg`, base));
    const renderer = new Resvg(svg, {
      fitTo: { mode: "width", value: 1600 },
      font: { fontBuffers: fonts, defaultFontFamily: "Barlow" }
    });
    const rendered = renderer.render();
    const png = Buffer.from(rendered.asPng());
    rendered.free();
    renderer.free();
    const destination = new URL(`${theme}.png`, base);
    if (check) {
      if (!png.equals(await readFile(destination))) {
        throw new Error(`Regenerate ${directory.name}/${theme}.png using npm run render`);
      }
    } else {
      await writeFile(destination, png);
    }
  }
}
