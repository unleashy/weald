import { Renderer } from "./lib/renderer.ts";
import spec from "./spec.yaml";

export const SPEC_HTML_FILE = `${import.meta.dirname}/spec.html`;

// language=html
const SHELL = `
<!DOCTYPE html>
<html lang="en-GB">
<head>
  <meta charset="utf-8">
  <link rel="stylesheet" href="style.css">
  <title>◊title</title>
</head>
<body class="body">
<header class="header">
  <h1>Weald 🌳<br><span class="subtitle">Language Specification</span></h1>
  <p class="metadata">Version ◊version • <time datetime="◊date">◊date</time></p>
</header>

<main class="main">
  <nav id="toc" class="toc">
    <h2><a href="#toc">Contents</a></h2>
    <div>
      ◊toc
    </div>
  </nav>
  <div class="content">
    ◊output
  </div>
</main>

</body>
</html>
`;

let date = new Date();
let output = new Renderer().render(spec, SHELL, (output, toc, meta) => ({
  title: meta.title,
  version: meta.version,
  date: date.toISOString().split("T")[0],
  output,
  toc,
}));
await Bun.write(SPEC_HTML_FILE, output);
