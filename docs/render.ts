import { Renderer } from "./lib/renderer.ts";
import spec from "./spec.yaml" with { type: "yaml" };
import html from "./spec.html" with { type: "html" };

const OUTPUT_PATH = `${import.meta.dirname}/spec.html`;

let output = new Renderer().render(spec);
await Bun.write(OUTPUT_PATH, output);

let server = Bun.serve({
  routes: {
    "/": html,
  },
  development: true,
});

console.log(`• Listening at ${server.url}`);
