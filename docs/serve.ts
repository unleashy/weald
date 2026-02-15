import "./render.ts";

let server = Bun.serve({
  routes: {
    "/": (await import("./spec.html", { with: { type: "html" } })).default,
  },
  development: process.env.NODE_ENV !== "production",
});

console.log(`• Listening at ${server.url}`);
