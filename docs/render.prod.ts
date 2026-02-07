import * as fs from "node:fs/promises";
import * as path from "node:path";
import { SPEC_HTML_FILE } from "./render.ts";

const OUT_DIR = `${import.meta.dirname}/dist/weald`;

await fs.rm(OUT_DIR, { recursive: true, force: true });
await fs.mkdir(OUT_DIR, { recursive: true });
let result = await Bun.build({
  entrypoints: [SPEC_HTML_FILE],
  outdir: OUT_DIR,
  naming: { entry: "[dir]/index.[ext]" },
  target: "browser",
  minify: true,
  splitting: true,
});

if (result.success) {
  // Find JS outputs, remove them from the html (we don't need any js)
  let entry = result.outputs.find((o) => o.path.endsWith(".html"));
  let jss = result.outputs.filter((o) => o.path.endsWith(".js"));
  if (entry === undefined) throw new Error("No entry point");

  let html = await fs.readFile(entry.path, "utf-8");
  html = html.replaceAll(/<script.*?src="\.\/(.+?)".*?><\/script>/gu, (_, name) => {
    let jsMatch = jss.find((js) => name === path.basename(js.path));
    if (jsMatch === undefined) throw new Error(`JS not found for ${name}`);

    return "";
  });

  await fs.writeFile(entry.path, html);
  await Promise.all(jss.map((js) => fs.unlink(js.path)));
}
