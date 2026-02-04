import html from "./spec.html" with { type: "html" };
import "./render.ts";

let server = Bun.serve({
  routes: {
    "/": html,
  },
  development: process.env.NODE_ENV !== "production",
});

console.log(`• Listening at ${server.url}`);
