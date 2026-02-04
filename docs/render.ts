import { Renderer } from "./lib/renderer.ts";
import spec from "./spec.yaml";

const OUTPUT_PATH = `${import.meta.dirname}/spec.html`;

let output = new Renderer().render(spec);
await Bun.write(OUTPUT_PATH, output);
