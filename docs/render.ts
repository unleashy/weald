import Slugger, { slug } from "github-slugger";
import { unicodeName } from "unicode-name";
import { parseTerms, type RootTerm, type Term } from "./lib/grammar.ts";
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
type Ref =
  | { kind: "section"; name: string; slug: string; levelStack: number[] }
  | { kind: "syntax"; name: string; slug: string };

type RefOf<K extends Ref["kind"]> = Extract<Ref, { kind: K }>;

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
    let section = this.#findRefOf("section", header);
    if (section === undefined) {
      return this.#renderError(`Section not found: '${header}'`);
    }

    let slug = section.slug;
    let name = section.name;

    let tag = `h${section.levelStack.length}`;
    let dottedLevel = section.levelStack.toReversed().join(".");
    let prefix = this.#isNumbered(section.name) ? `${dottedLevel}&nbsp;` : "";

    this.#output.push(`<section id="${slug}" class="prose">`);
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

      case "syn":
        return this.#renderSyntax(value);

      default:
        return this.#renderError(`Unknown tag ◊${name}`);
    }
  }

  #renderToc() {
    let sections: Array<RefOf<"section">> = this.#refs.filter((s) => s.kind === "section");

    const childrenOfLevel = (levelStack: number[]) => {
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

  #renderSyntax(value: unknown) {
    if (!isObject(value)) {
      this.#renderError("◊syn tag with non-object contents");
      return;
    }

    let caption = "Grammar forms";
    if (typeof value["◊caption"] === "string") {
      caption += ` for ${value["◊caption"]}`;
    }

    this.#output.push(`<figure class="syntax">`);
    this.#output.push(`<dl class="ruleset">`);

    for (let [rule, defs] of Object.entries(value)) {
      if (rule.startsWith("◊")) continue;

      let ref = this.#findRefOf("syntax", rule);
      if (ref === undefined) {
        this.#renderError(`Syntax rule not found: '${rule}'`);
        continue;
      }

      this.#output.push(`<div class="rule">`);
      this.#output.push(
        `<dt class="rule-name">` +
          `<a href="#${ref.slug}" class="syntax-nonterminal"><dfn id="${ref.slug}">${rule}</dfn></a>` +
          `</dt>`,
      );

      if (!Array.isArray(defs)) {
        defs = [defs];
      }

      for (let def of (defs as unknown[]).map(String)) {
        this.#output.push(`<dd class="rule-def">`);

        if (def.startsWith("◊p")) {
          this.#output.push(`<span class="syntax-prose">`);
          this.#output.push(this.#renderInlineTags(def.substring(2).trim(), "syn"));
        } else {
          this.#output.push(`<span>`);
          this.#output.push(this.#renderSyntaxDef(def));
        }

        this.#output.push(`</span>`);
        this.#output.push(`</dd>`);
      }

      this.#output.push("</div>");
    }

    this.#output.push("</dl>");
    this.#output.push(`<figcaption class="syntax-caption">${caption}</figcaption>`);
    this.#output.push("</figure>");
  }

  #renderSyntaxDef(def: string): string {
    let renderRootTerm: (s: string[], term: RootTerm) => void;
    let renderTerms: (s: string[], terms: Term[]) => void;
    let renderTerm: (s: string[], term: Term) => void;

    renderRootTerm = (s: string[], term: RootTerm) => {
      if (term.type === "but-not") {
        s.push(`<span class="syntax-seq">`);
        renderTerms(s, term.left);
        s.push(`<span class="syntax-but-not">but not</span>`);
        renderTerms(s, term.right);
        s.push(`</span>`);
      } else {
        renderTerms(s, term.terms);
      }
    };

    renderTerms = (s: string[], terms: Term[]) => {
      s.push(`<span class="syntax-seq">`);
      for (let term of terms) renderTerm(s, term);
      s.push(`</span>`);
    };

    renderTerm = (s: string[], term: Term) => {
      if (term.quantifier) {
        s.push(`<span>`);
      }

      if (term.type === "group") {
        s.push(`<span class="syntax-parens">(</span>`);
        renderRootTerm(s, term.value);
        s.push(`<span class="syntax-parens">)</span>`);
      } else if (term.type === "nonterminal") {
        let ref = this.#findRefOf("syntax", term.value);
        if (ref === undefined) {
          s.push(errorTag(`Unknown ref '${term.value}'`));
          return;
        }

        s.push(`<a href="#${ref.slug}" class="syntax-${term.type}">${ref.name}</a>`);
      } else if (term.type === "unicode") {
        s.push(
          `<abbr title="${nameCodePoint(term.value)}" class="syntax-unicode">U+${term.value}</abbr>`,
        );
      } else {
        s.push(`<span class="syntax-${term.type}">${term.value}</span>`);
      }

      if (term.quantifier) {
        s.push(`<span class="syntax-quantifier">${term.quantifier}</span>`);
        s.push(`</span>`);
      }
    };

    let rootTerm;
    try {
      rootTerm = parseTerms(def);
    } catch (e) {
      if (!(e instanceof SyntaxError)) throw e;
      return errorTag(e.message);
    }

    let s: string[] = [];
    renderRootTerm(s, rootTerm);
    return s.join("");
  }

  #renderError(message: string) {
    this.#output.push(`<p>${errorTag(`Error: ${message}`)}</p>`);
  }

  #renderInlineTags(s: string, defaultTag: string = "a"): string {
    return s.replaceAll(/◊(\w*)(?:\[(.*?)\])?(?:\[(.*?)\])?/gu, (_, tag, content, extra) => {
      if (tag === "" || tag === undefined) tag = defaultTag;
      return this.#renderInlineTag(tag, content, extra);
    });
  }

  #renderInlineTag(tag: string, content: string | undefined, extra: string | undefined): string {
    switch (tag) {
      case "":
      case "a":
        return this.#renderLink(content, extra);

      case "syn":
        return this.#renderSyntaxLink(content, extra);

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

    let ref = this.#findRef(href);
    if (ref === undefined) {
      return errorTag(`◊a: Unknown ref '${href}' (slug: '${slug(href)}')`);
    }

    href = `#${ref.slug}`;

    return `<a href="${href}">${body}</a>`;
  }

  #renderSyntaxLink(body: string | undefined, href: string | undefined): string {
    if (body === undefined) return errorTag("◊syn tag missing body");

    href ??= body;

    let ref = this.#findRefOf("syntax", href);
    if (ref === undefined) {
      return errorTag(`◊syn: Unknown ref '${href}' (slug: '${slug(href)}')`);
    }

    href = `#${ref.slug}`;

    return `<a href="${href}" class="syntax-nonterminal">${body}</a>`;
  }

  #renderAbbreviation(abbr: string | undefined, body: string | undefined): string {
    if (abbr === undefined) return errorTag("◊abbr tag missing abbreviation");
    if (body === undefined) return errorTag("◊abbr tag missing body");

    return `<abbr title="${body}">${abbr}</abbr>`;
  }

  #renderItalics(s: string | undefined): string {
    if (s === undefined) return errorTag("◊i tag missing body");
    return `<em>${s}</em>`;
  }

  #renderUnicode(s: string | undefined): string {
    if (s === undefined) return errorTag("◊u tag missing body");

    return `<abbr title="${nameCodePoint(s)}" class="unicode">U+${s}</abbr>`;
  }

  #collectRefs(children: Children, levelStack: number[]) {
    for (let child of children) {
      for (let [key, value] of Object.entries(child)) {
        if (key.startsWith("◊")) {
          if (key === "◊syn" && typeof value === "object" && value !== null) {
            this.#collectSyntaxRefs(value as Record<string, unknown>);
          }
        } else {
          if (!Array.isArray(value)) continue;

          this.#collectSectionRefs(key, value, levelStack);
        }
      }
    }
  }

  #collectSyntaxRefs(rules: Record<string, unknown>) {
    for (let [name, _] of Object.entries(rules)) {
      this.#addSyntaxRef(name);
    }
  }

  #collectSectionRefs(name: string, children: Children, levelStack: number[]) {
    let ref = this.#addSectionRef(name, levelStack);
    this.#collectRefs(children, [1, ...levelStack]);
    if (this.#isNumbered(ref.name)) ++levelStack[0];
  }

  #findRef(name: string): Ref | undefined {
    let nameSlug = slug(name);
    return this.#refs.find((s) => slug(s.name) === nameSlug);
  }

  #findRefOf<K extends Ref["kind"]>(kind: K, name: string): RefOf<K> | undefined {
    let nameSlug = slug(name);
    return this.#refs.find((s) => s.kind === kind && slug(s.name) === nameSlug) as RefOf<K>;
  }

  #addSectionRef(name: string, levelStack: number[]): Ref {
    let ref: Ref = {
      name,
      slug: this.#slugger.slug(`sec-${name}`),
      kind: "section",
      levelStack: levelStack.slice(),
    };
    this.#refs.push(ref);
    return ref;
  }

  #addSyntaxRef(name: string): Ref {
    let ref: Ref = {
      name,
      slug: this.#slugger.slug(`syn-${name}`),
      kind: "syntax",
    };
    this.#refs.push(ref);
    return ref;
  }

  #isNumbered(name: string): boolean {
    return !this.#nonNumberedHeadings.includes(name);
  }
}

function errorTag(message: string): string {
  return `<strong style="color: red">${message}</strong>`;
}

function escapeHtml(s: string): string {
  return s.replaceAll("&", "&amp;").replaceAll("<", "&lt;");
}

function nameCodePoint(s: string): string {
  return (
    unicodeName(String.fromCodePoint(Number.parseInt(s, 16)))?.toLowerCase() ?? "unknown character"
  );
}

function isObject(x: unknown): x is Record<string, unknown> {
  return typeof x === "object" && x !== null;
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
