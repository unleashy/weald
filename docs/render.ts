import Slugger, { slug } from "github-slugger";
import spec from "./spec.yaml" with { type: "yaml" };
import html from "./spec.html" with { type: "html" };

const OUTPUT_PATH = `${import.meta.dirname}/spec.html`;

// language=html
const BOILERPLATE = `
<!DOCTYPE html>
<head>
  <meta charset="utf-8">
  <title>◊title</title>
  <link rel="stylesheet" href="style.css">
</head>

<header>
  <p class="title">◊title</p>
  <p class="metadata">Version ◊version / ◊date</p>
</header>
`;

type Children = Array<Record<string, unknown> | string>;
interface Ref {
  name: string;
  slug: string;
  kind: "section";
  levelStack: number[];
}

class Renderer {
  readonly #slugger: Slugger = new Slugger();
  #output: string[] = [];
  #refs: Ref[] = [];
  #nonNumberedHeadings: string[] = [];

  render(spec: Array<Record<string, unknown>>): string {
    let renderDate = new Date().toISOString().split("T")[0];
    let meta = spec[0]["◊meta"] as Record<string, unknown>;

    this.#output = [BOILERPLATE];
    this.#refs = [];
    this.#nonNumberedHeadings = Array.isArray(meta["non-numbered-headings"])
      ? meta["non-numbered-headings"]
      : [];

    this.#collectRefs(spec.slice(1), [1]);
    this.#renderChildren(spec.slice(1));

    return this.#output
      .join("\n")
      .replaceAll(/◊(\w+)/gu, (matched, prop) => {
        switch (prop) {
          case "title":
            return String(meta.title ?? "");
          case "version":
            return String(meta.version ?? "");
          case "date":
            return renderDate;
          default:
            return matched;
        }
      })
      .trim();
  }

  #renderChildren(children: unknown[]) {
    for (let child of children) {
      if (typeof child === "object") {
        let entries = Object.entries(child ?? {});
        if (entries.length !== 1) {
          let message =
            entries.length === 0 ? "Item with zero children" : "Item with multiple children";
          this.#renderError(message);
          continue;
        }

        let [key, value] = entries[0];
        if (key.startsWith("◊")) {
          this.#renderTag(key.substring(1), value);
        } else {
          this.#renderSection(key, value);
        }
      } else if (typeof child === "string") {
        this.#renderParagraph(child);
      } else {
        this.#renderError(`Invalid child type ${typeof child}`);
      }
    }
  }

  #renderSection(header: string, value: unknown) {
    let section = this.#findRefByName(header);
    if (section === undefined) {
      return this.#renderError(`Section not found: '${header}'`);
    }

    let slug = section.slug;
    let name = section.name;

    let tag = `h${section.levelStack.length}`;
    let dottedLevel = section.levelStack.toReversed().join(".");
    let prefix = this.#isNumbered(section.name) ? `${dottedLevel}&nbsp;` : "";

    this.#output.push(`<section id="${slug}">`);
    this.#output.push(`<${tag}><a href="#${slug}">${prefix}${name}</a></${tag}>`);

    if (Array.isArray(value)) {
      this.#renderChildren(value);
    } else if (typeof value === "string") {
      this.#renderTag("p", value);
    }

    this.#output.push(`</section>`);
  }

  #renderTag(name: string, value: unknown) {
    switch (name) {
      case "toc":
        return this.#renderToc();

      case "p":
        return this.#renderParagraph(value);

      default:
        return this.#renderError(`Unknown tag ◊${name}`);
    }
  }

  #renderToc() {
    let sections = this.#refs.filter((s) => s.kind === "section");

    const childrenOfLevel = (levelStack: number[]): Ref[] => {
      return sections
        .filter((it) => Bun.deepEquals(levelStack, it.levelStack.slice(1)))
        .filter((it) => this.#isNumbered(it.name));
    };

    const renderTocSection = (levelStack: number[]) => {
      let children = childrenOfLevel(levelStack);
      if (children.length === 0) return;

      this.#output.push("<ol>");
      for (let section of children) {
        this.#output.push(`<li><a href="#${section.slug}">${section.name}</a>`);
        renderTocSection(section.levelStack);
        this.#output.push("</li>");
      }
      this.#output.push("</ol>");
    };

    this.#output.push("<nav>");
    renderTocSection([]);
    this.#output.push("</nav>");
  }

  #renderParagraph(value: unknown) {
    if (typeof value !== "string") {
      this.#renderError("◊p tag with non-string contents");
      return;
    }

    this.#output.push(
      ...value
        .trim()
        .split("\n")
        .map((p) => p.trim())
        .filter((p) => p.length > 0)
        .map(escapeHtml)
        .map((p) => this.#renderInlineTags(p))
        .map((p) => `<p>${p}</p>`),
    );
  }

  #renderError(message: string) {
    this.#output.push(`<p>${errorTag(message)}</p>`);
  }

  #renderInlineTags(s: string): string {
    return s.replaceAll(/◊(\w*)(?:\[(.*?)\])?(?:\[(.*?)\])?/gu, (_, tag, content, extra) =>
      this.#renderInlineTag(tag, content, extra),
    );
  }

  #renderInlineTag(tag: string, content: string | undefined, extra: string | undefined): string {
    switch (tag) {
      case "":
      case "a":
        return this.#renderLink(content, extra);

      case "abbr":
        return this.#renderAbbreviation(content, extra);

      case "i":
        return this.#renderItalics(content);

      case "u":
      case "U":
        return this.#renderUnicode(content);

      default:
        return errorTag(`Unknown inline tag ◊${tag}`);
    }
  }

  #renderLink(body: string | undefined, href: string | undefined): string {
    if (body === undefined) return errorTag("◊a tag missing body");

    href ??= body;

    let ref = this.#findRefByName(href);
    if (ref === undefined) {
      return errorTag(`Unknown ref '${href}' (slug: '${slug(href)}')`);
    }

    href = `#${ref.slug}`;

    return `<a href="${href}">${body}</a>`;
  }

  #renderAbbreviation(body: string | undefined, abbr: string | undefined): string {
    if (body === undefined) return errorTag("◊abbr tag missing body");
    if (abbr === undefined) return errorTag("◊abbr tag missing abbreviation");

    return `<abbr title="${body}">${abbr}</abbr>`;
  }

  #renderItalics(s: string | undefined): string {
    if (s === undefined) return errorTag("◊i tag missing body");
    return `<em>${s}</em>`;
  }

  #renderUnicode(s: string | undefined): string {
    if (s === undefined) return errorTag("◊u tag missing body");
    return `<i class="unicode">U+${s}</i>`;
  }

  #collectRefs(children: Children, levelStack: number[]) {
    for (let child of children) {
      for (let [key, value] of Object.entries(child)) {
        if (!key.startsWith("◊") && Array.isArray(value)) {
          let ref = this.#addRef(key, levelStack);
          this.#collectRefs(value, [1, ...levelStack]);
          if (this.#isNumbered(ref.name)) ++levelStack[0];
        }
      }
    }
  }

  #addRef(name: string, levelStack: number[]): Ref {
    let ref: Ref = {
      name,
      slug: `sec-${this.#slugger.slug(name)}`,
      kind: "section",
      levelStack: levelStack.slice(),
    };
    this.#refs.push(ref);
    return ref;
  }

  #findRefByName(name: string): Ref | undefined {
    let nameSlug = slug(name);
    return this.#refs.find((s) => slug(s.name) === nameSlug);
  }

  #isNumbered(name: string): boolean {
    return !this.#nonNumberedHeadings.includes(name);
  }
}

function errorTag(message: string): string {
  return `<strong style="color: red">Error: ${message}</strong>`;
}

function escapeHtml(s: string): string {
  return s.replaceAll("&", "&amp;").replaceAll("<", "&lt;");
}

async function go() {
  let output = new Renderer().render(spec);
  await Bun.write(OUTPUT_PATH, output);

  let server = Bun.serve({
    routes: {
      "/": html,
    },
    development: true,
  });

  console.log(`• Listening at ${server.url}`);
}

await go();
