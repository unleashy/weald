import * as fs from "node:fs/promises";
import * as path from "node:path";
import * as yaml from "yaml";
import { defineConfig, type Plugin } from "vite";
import { render } from "./docs/lib/renderer.ts";

const SPEC_PATH = `${import.meta.dirname}/docs/spec.yaml`;

function yamlSpecPlugin(): Plugin {
  return {
    name: "yaml-spec",
    transformIndexHtml: {
      order: "pre",
      async handler(html) {
        let spec = yaml
          .parseAllDocuments(await fs.readFile(SPEC_PATH, "utf8"))
          .map((doc) => doc.toJS());
        let date = new Date();
        return render(spec, html, (renderer, meta) => ({
          title: meta.title,
          version: meta.version,
          date: date.toISOString().split("T")[0],
          output: renderer.renderSpec(),
          toc: renderer.renderToc(),
        }));
      },
    },

    configureServer(server) {
      server.watcher.add(SPEC_PATH);
      server.watcher.on("change", (file) => {
        if (path.normalize(file) === path.normalize(SPEC_PATH)) {
          server.ws.send({ type: "full-reload" });
        }
      });
    },
  };
}

export default defineConfig({
  plugins: [yamlSpecPlugin()],
  root: "./docs",
  base: "/weald",
  build: {
    emptyOutDir: true,
    outDir: `${import.meta.dirname}/docs/dist/weald`,
  },
  css: {
    transformer: "lightningcss",
  },
});
