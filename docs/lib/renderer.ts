import Slugger, { slug } from "github-slugger";
import { unicodeName } from "unicode-name";
import * as csv from "csv-parse/sync";
import { parseTerms, type RootTerm, type Term } from "./grammar.ts";

type Children = Array<Record<string, unknown> | string>;

interface Ref {
  name: string;
  slug: string;
}

interface SectionRef extends Ref {
  levelStack: number[];
}

interface SyntaxRef extends Ref {}

export class Renderer {
  readonly #slugger: Slugger = new Slugger();
  #output: string[] = [];
  #sectionRefs: SectionRef[] = [];
  #syntaxRefs: Map<string, SyntaxRef> = new Map();
  #nonNumberedHeadings: string[] = [];

  render(
    spec: Array<Record<string, unknown>>,
    shell: string,
    getProps: (
      output: string,
      toc: string,
      meta: Record<string, string>,
    ) => Record<string, string | undefined>,
  ): string {
    let meta = spec[0]?.["◊meta"] as Record<string, string> | undefined;
    if (!isObject(meta)) {
      throw new Error("No metadata found");
    }

    let rest = spec.slice(1);

    this.#nonNumberedHeadings = Array.isArray(meta["non-numbered-headings"])
      ? meta["non-numbered-headings"]
      : [];

    this.#collectRefs(rest);
    let out = this.#renderOutput(rest);
    let toc = this.#renderToc();

    let props = getProps(out, toc, meta);
    return shell.replaceAll(/◊(\w+)/gu, (s, prop) => props[prop] ?? s).trim();
  }

  #renderToc(): string {
    let tocOutput: string[] = [];

    const childrenOfLevel = (levelStack: number[]) => {
      return this.#sectionRefs
        .filter((it) => Bun.deepEquals(levelStack, it.levelStack.slice(1)))
        .filter((it) => this.#isNumbered(it.name));
    };

    const renderTocSection = (levelStack: number[]) => {
      let children = childrenOfLevel(levelStack);
      if (children.length === 0) return;

      tocOutput.push("<ol>");
      for (let section of children) {
        tocOutput.push(`<li><a href="#${section.slug}">${section.name}</a>`);
        renderTocSection(section.levelStack);
        tocOutput.push("</li>");
      }
      tocOutput.push("</ol>");
    };

    renderTocSection([]);
    return tocOutput.join("\n");
  }

  #renderOutput(spec: unknown[]): string {
    this.#output = [];
    this.#renderChildren(spec, 1);
    return this.#output.join("\n");
  }

  #renderChildren(children: unknown[], level: number) {
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
          this.#renderSection(key, value, level);
        }
      } else if (typeof child === "string") {
        this.#renderParagraph(child);
      } else {
        this.#renderError(`Invalid child type ${typeof child}`);
      }
    }
  }

  #renderSection(header: string, value: unknown, level: number) {
    let sections = this.#findSectionRef(header).filter((it) => it.levelStack.length === level);
    if (sections.length === 0) {
      return this.#renderError(`Section not found: '${header}'`);
    } else if (sections.length > 1) {
      return this.#renderError(`Ambiguous section '${header}'`);
    }

    let section = sections[0];
    let slug = section.slug;
    let name = section.name;

    let tag = `h${1 + section.levelStack.length}`;
    let dottedLevel = section.levelStack.toReversed().join(".");
    let prefix = this.#isNumbered(section.name)
      ? `<span class="heading-level">${dottedLevel}</span>&nbsp;`
      : "";

    this.#output.push(`<section id="${slug}" class="prose">`);
    this.#output.push(`<${tag}><a href="#${slug}">${prefix}${name}</a></${tag}>`);

    if (Array.isArray(value)) {
      this.#renderChildren(value, level + 1);
    } else if (typeof value === "string") {
      this.#renderTag("p", value);
    }

    this.#output.push(`</section>`);
  }

  #renderTag(name: string, value: unknown) {
    switch (name) {
      case "csv":
        return this.#renderCsvTable(value);

      case "dl":
        return this.#renderDefinitionList(value);

      case "note":
        return this.#renderNote(value);

      case "ol":
        return this.#renderOrderedList(value);

      case "p":
        return this.#renderParagraph(value);

      case "syn":
        return this.#renderSyntax(value);

      case "ul":
        return this.#renderUnorderedList(value);

      default:
        return this.#renderError(`Unknown tag ◊${name}`);
    }
  }

  #renderCsvTable(table: unknown) {
    if (!isObject(table)) {
      this.#renderError("◊csv tag with non-object contents");
      return;
    }

    let data = table["◊data"];
    if (typeof data !== "string") {
      this.#renderError("◊csv tag with no data");
      return;
    }

    let result = parseCsv(data);
    if (result.error) {
      this.#renderError(`◊csv: ${result.error.message}`);
      return;
    }

    let { header, rows } = result;

    let caption;
    if (typeof table["◊caption"] === "string") {
      caption = `Table: ${table["◊caption"]}`;
    }

    this.#output.push(`<table>`);
    if (caption) this.#output.push(`<caption>${caption}</caption>`);

    this.#output.push("<thead>");
    this.#output.push("<tr>");
    for (let column of header) {
      this.#output.push(`<th scope="col">${this.#renderInlineTags(column)}</th>`);
    }
    this.#output.push("</tr>");
    this.#output.push("</thead>");

    this.#output.push("<tbody>");
    for (let row of rows) {
      this.#output.push("<tr>");
      for (let cell of row) {
        this.#output.push(`<td>${this.#renderInlineTags(cell)}</td>`);
      }
      this.#output.push("</tr>");
    }
    this.#output.push("</tbody>");

    this.#output.push("</table>");
  }

  #renderDefinitionList(value: unknown) {
    if (!isObject(value)) {
      this.#renderError("◊dl tag with non-object contents");
      return;
    }

    this.#output.push("<dl>");
    for (let [term, def] of Object.entries(value)) {
      let renderedTerm = this.#renderInlineTags(term);
      let renderedDef = this.#renderInlineTags(String(def));
      this.#output.push(`<div><dt>${renderedTerm}</dt><dd>${renderedDef}</dd></div>`);
    }
    this.#output.push("</dl>");
  }

  #renderNote(value: unknown) {
    if (typeof value !== "string") {
      this.#renderError("◊note tag with non-string contents");
      return;
    }

    this.#output.push(`<aside class="note"><strong class="colour:accent-12">Note:</strong>`);
    this.#output.push(`<div>`);
    this.#renderParagraph(value);
    this.#output.push(`</div>`);
    this.#output.push(`</aside>`);
  }

  #renderOrderedList(value: unknown) {
    this.#renderList("ol", value);
  }

  #renderUnorderedList(value: unknown) {
    this.#renderList("ul", value);
  }

  #renderList(tag: "ul" | "ol", value: unknown) {
    if (!Array.isArray(value)) {
      this.#renderError(`◊${tag} tag with non-string contents`);
      return;
    }

    this.#output.push(`<${tag}>`);
    for (let item of value) {
      this.#output.push(`<li>`);
      this.#renderParagraph(item);
      this.#output.push(`</li>`);
    }
    this.#output.push(`</${tag}>`);
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

  #renderSyntax(ruleset: unknown) {
    if (!isObject(ruleset)) {
      this.#renderError("◊syn tag with non-object contents");
      return;
    }

    let caption = "Grammar forms";
    if (typeof ruleset["◊caption"] === "string") {
      caption += ` for ${ruleset["◊caption"]}`;
    }

    this.#output.push(`<figure class="syntax">`);
    this.#output.push(`<figcaption class="syntax-caption">${caption}</figcaption>`);
    this.#renderRuleset(ruleset);
    this.#output.push("</figure>");
  }

  #renderRuleset(value: Record<string, unknown>) {
    function normaliseRuleChoices(choices: unknown): string[] {
      if (!Array.isArray(choices)) {
        choices = [choices];
      }

      return (choices as unknown[]).filter(Boolean).map(String);
    }

    this.#output.push(`<dl class="ruleset">`);

    for (let [rule, choices] of Object.entries(value)) {
      if (!this.#renderRuleHeader(rule)) continue;

      for (let choice of normaliseRuleChoices(choices)) {
        this.#output.push(`<dd class="rule-def">`);

        let [_, tag, rest] = choice.match(/^(◊p|◊one-of)?\s*(.*)/u)!;
        if (tag === "◊p") {
          this.#renderRuleProse(rest);
        } else if (tag === "◊one-of") {
          this.#renderRuleOneOf(rest);
        } else {
          this.#renderRuleChoice(choice);
        }

        this.#output.push(`</dd>`);
      }

      this.#output.push(`</div>`);
    }

    this.#output.push(`</dl>`);
  }

  #renderRuleProse(rest: string) {
    this.#output.push(`<span class="syntax-prose">`);
    this.#output.push(this.#renderInlineTags(rest.trim(), "syn"));
    this.#output.push(`</span>`);
  }

  #renderRuleOneOf(rest: string) {
    let terminals = rest.trim().split(/\s+/);

    this.#output.push(`<span>`);
    this.#output.push(`<span class="syntax-one-of">one of</span>`);
    this.#output.push(`<span class="syntax-seq">`);
    for (let t of terminals) this.#output.push(`<span class="syntax-terminal">${t}</span>`);
    this.#output.push(`</span>`);
    this.#output.push(`</span>`);
  }

  #renderRuleChoice(choice: string) {
    this.#output.push(`<span>`);
    this.#output.push(this.#renderGrammar(choice));
    this.#output.push(`</span>`);
  }

  #renderRuleHeader(rule: string): boolean {
    if (rule.startsWith("◊")) return false;

    let ref = this.#findSyntaxRef(rule);
    if (ref === undefined) {
      this.#renderError(`Syntax rule not found: '${rule}'`);
      return false;
    }

    this.#output.push(`<div class="rule">`);
    this.#output.push(
      `<dt class="rule-name">` +
        `<a href="#${ref.slug}" class="syntax-nonterminal"><dfn id="${ref.slug}">${rule}</dfn></a>` +
        `</dt>`,
    );

    return true;
  }

  #renderGrammar(def: string): string {
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
      s.push(`<span>`);

      if (term.type === "group") {
        s.push(`<span class="syntax-parens">(</span>`);
        renderRootTerm(s, term.value);
        s.push(`<span class="syntax-parens">)</span>`);
      } else if (term.type === "nonterminal") {
        let ref = this.#findSyntaxRef(term.value);
        if (ref === undefined) {
          s.push(errorTag(`Unknown ref '${term.value}'`));
          s.push(`</span>`);
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
      }

      s.push(`</span>`);
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
    let result = "";

    while (s.length > 0) {
      let rendered;
      [rendered, s] = this.#renderNextInlineTag(s, defaultTag);
      result += rendered;
    }

    return result;
  }

  #renderNextInlineTag(s: string, defaultTag: string): [string, string] {
    let tagMatch = s.match(/◊(\w*)\[/u);
    if (tagMatch?.index === undefined) return [s, ""];

    let beforeTag = s.slice(0, tagMatch.index);
    s = s.slice(tagMatch.index + tagMatch[0].length);

    let content;
    [content, s] = this.#renderNestedInlineTag(s, defaultTag);

    let extraMatch = s.match(/^\[(.*?)\]/u);
    if (extraMatch) s = s.slice(extraMatch[0].length);

    let tag = tagMatch[1] || defaultTag;
    let extra = extraMatch?.[1];
    let rendered = this.#renderInlineTag(tag, content, extra);

    return [beforeTag + rendered, s];
  }

  #renderNestedInlineTag(s: string, defaultTag: string): [string, string] {
    let content = "";

    while (s.length > 0) {
      // find boundary (tag close or next inner tag)
      let contentMatch = s.match(/^.*?(?=\]|◊\w*\[)/u);
      if (!contentMatch) {
        content += s;
        s = "";
        break;
      }

      content += contentMatch[0];
      s = s.slice(contentMatch[0].length);

      if (s[0] === "◊") {
        // nested tag: recursively parse it
        let rendered;
        [rendered, s] = this.#renderNextInlineTag(s, defaultTag);
        content += rendered;
      } else if (s[0] === "]") {
        // closing bracket: end of content
        s = s.slice(1);
        break;
      }
    }

    return [content, s];
  }

  #renderInlineTag(tag: string, content: string | undefined, extra: string | undefined): string {
    switch (tag) {
      case "":
      case "a":
        return this.#renderLink(content, extra);

      case "abbr":
        return this.#renderAbbreviation(content, extra);

      case "b":
        return this.#renderBold(content);

      case "i":
        return this.#renderItalics(content);

      case "syn":
        return this.#renderSyntaxLink(content, extra);

      case "tt":
        return this.#renderTerminal(content);

      case "u":
      case "U":
        return this.#renderUnicode(content);

      default:
        return this.#renderGeneric(tag, content);
    }
  }

  #renderLink(body: string | undefined, href: string | undefined): string {
    if (body === undefined) return errorTag("◊a tag missing body");

    href ??= body;

    if (href.startsWith("https://") || href.startsWith("http://")) {
      return `<a href="${href}" target="_blank" rel="noopener noreferrer external">${body}</a>`;
    } else {
      let ref = this.#findSectionRef(href);
      if (ref.length === 0) {
        return errorTag(`◊a: Unknown ref '${href}' (slug: '${slug(href)}')`);
      } else if (ref.length > 1) {
        return errorTag(`◊a: Ambiguous ref '${href}' (slug: '${slug(href)}')`);
      }

      return `<a href="#${ref[0].slug}">${body}</a>`;
    }
  }

  #renderSyntaxLink(body: string | undefined, href: string | undefined): string {
    if (body === undefined) return errorTag("◊syn tag missing body");

    href ??= body;

    let ref = this.#findSyntaxRef(href);
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

  #renderBold(s: string | undefined): string {
    if (s === undefined) return errorTag("◊b tag missing body");

    return `<strong>${s}</strong>`;
  }

  #renderItalics(s: string | undefined): string {
    if (s === undefined) return errorTag("◊i tag missing body");
    return `<em>${s}</em>`;
  }

  #renderTerminal(s: string | undefined): string {
    if (s === undefined) return errorTag("◊tt tag missing body");
    return `<code class="syntax-terminal">${s}</code>`;
  }

  #renderUnicode(s: string | undefined): string {
    if (s === undefined) return errorTag("◊u tag missing body");

    return `<abbr title="${nameCodePoint(s)}" class="unicode">U+${s}</abbr>`;
  }

  #renderGeneric(tag: string, s: string | undefined): string {
    if (s === undefined) return errorTag(`◊${tag} tag missing body`);

    return `<${tag}>${s}</${tag}>`;
  }

  #collectRefs(children: Children) {
    this.#sectionRefs = [];
    this.#syntaxRefs = new Map();
    this.#collectRefsWithLevel(children, [1]);
  }

  #collectRefsWithLevel(children: Children, levelStack: number[]) {
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
      if (name.startsWith("◊")) continue;
      if (!this.#addSyntaxRef(name)) {
        this.#alert(`Duplicate syntax reference '${name}'`);
      }
    }
  }

  #collectSectionRefs(name: string, children: Children, levelStack: number[]) {
    let ref = this.#addSectionRef(name, levelStack);
    if (!ref) {
      this.#alert(`Duplicate section reference '${name}'`);
    }

    this.#collectRefsWithLevel(children, [1, ...levelStack]);
    if (ref && this.#isNumbered(ref.name)) ++levelStack[0];
  }

  #findSectionRef(name: string): SectionRef[] {
    let nameSlug = slug(name);
    return this.#sectionRefs.filter((s) => slug(s.name) === nameSlug);
  }

  #findSyntaxRef(name: string): SyntaxRef | undefined {
    return this.#syntaxRefs.get(slug(name, true));
  }

  #addSectionRef(name: string, levelStack: number[]): SectionRef {
    let ref: SectionRef = {
      name,
      slug: this.#slugger.slug(`sec-${name}`, true),
      levelStack: levelStack.slice(),
    };

    this.#sectionRefs.push(ref);

    return ref;
  }

  #addSyntaxRef(name: string): SyntaxRef | undefined {
    let s = slug(name, true);
    if (this.#syntaxRefs.has(s)) return undefined;

    let ref: SyntaxRef = {
      name,
      slug: `syn-${s}`,
    };

    this.#syntaxRefs.set(s, ref);
    return ref;
  }

  #isNumbered(name: string): boolean {
    return !this.#nonNumberedHeadings.includes(name);
  }

  #alert(message: string) {
    this.#output.push(`<p>${errorTag(`Ref collection error: ${message}`)}</p>`);
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

function parseCsv(data: string) {
  try {
    let header: string[] = [];
    const columns = (h: string[]) => {
      header = h;
      return h;
    };

    let rows = csv.parse(data, { columns, trim: true, skip_empty_lines: true });

    return {
      header,
      rows: rows.map((obj) => Object.values(obj as Record<string, string>)),
    };
  } catch (error) {
    if (!(error instanceof csv.CsvError)) throw error;

    return { error };
  }
}
