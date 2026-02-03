export type Quantifier = "*" | "+" | "?";

export type Term =
  | {
      type: "nonterminal" | "terminal" | "unicode";
      value: string;
      quantifier?: Quantifier;
    }
  | { type: "group"; value: Term[]; quantifier?: Quantifier };

export function parseTerms(rule: string): Term[] {
  return root(rule);
}

// Root: Seq End
// Ws: [\s]*
function root(s: string) {
  let [result, end] = seq(s);
  if (!/^\s*$/.test(end)) {
    throw new SyntaxError(`expected end of input at ${JSON.stringify(end)}`);
  }

  return result;
}

// Seq: QuantifiableTerm*
function seq(s: string) {
  let terms: Term[] = [];

  let qt;
  while (([qt, s] = quantifiableTerm(s)) && qt) {
    terms.push(qt);
  }

  return [terms, s] as const;
}

// QuantifiableTerm: Term Quantifier?
function quantifiableTerm(s: string) {
  let r = primaryTerm(s);

  let value;
  [value, s] = r;
  if (!value) return r;

  let r2 = quantifier(s);

  let quant;
  [quant, s] = r2;

  return [{ ...value, quantifier: quant }, s] as const;
}

// Quantifier: Ws [*+?]
function quantifier(s: string) {
  let m = s.match(/^\s*([*+?])/);
  if (!m) return [undefined, s] as const;

  return [m[1] as Quantifier, s.slice(m[1].length)] as const;
}

// Term: Terminal | Nonterminal | Range | Group
function primaryTerm(s: string) {
  s = s.trimStart();
  let r = terminal(s) ?? nonterminal(s) ?? range(s) ?? group(s);
  if (r) {
    return r;
  } else {
    return [undefined, s] as const;
  }
}

// Terminal: Ws '```' | Ws '`' .+ '`' | Ws 'U+' [A-Fa-f0-9]+
function terminal(s: string) {
  if (s.startsWith("```")) {
    return [{ type: "terminal", value: "`" }, s.slice(3)] as const;
  } else if (s.startsWith("`")) {
    let end = s.indexOf("`", 1);
    if (end === -1) {
      throw new SyntaxError(`unclosed string: ${JSON.stringify(s)}`);
    }

    return [{ type: "terminal", value: s.slice(1, end) }, s.slice(end + 1)] as const;
  } else if (s.startsWith("U+")) {
    let m = s.match(/^U\+([0-9A-Fa-f]{1,6})/);
    if (!m) {
      throw new SyntaxError(`bad unicode code point: ${JSON.stringify(s)}`);
    }

    return [{ type: "unicode", value: m[1] }, s.slice(m[0].length)] as const;
  } else {
    return undefined;
  }
}

// Nonterminal: Ws [A-Za-z_] [A-Za-z0-9_]*
function nonterminal(s: string) {
  let m = s.match(/^[A-Za-z_][A-Za-z0-9_]*/);
  if (!m) return undefined;

  return [{ type: "nonterminal", value: m[0] }, s.slice(m[0].length)] as const;
}

// Range: Ws '[' .+ ']'
function range(s: string) {
  if (!s.startsWith("[")) return undefined;

  let end = s.indexOf("]", 1);
  if (end === -1) {
    throw new SyntaxError(`unclosed range: ${JSON.stringify(s)}`);
  }

  return [{ type: "terminal", value: s.slice(0, end + 1) }, s.slice(end + 1)] as const;
}

// Group: Ws '(' Seq Ws ')'
function group(s: string) {
  let prev = s;
  if (!s.startsWith("(")) return undefined;
  s = s.slice(1);

  let value;
  [value, s] = seq(s);

  s = s.trimStart();
  if (!s.startsWith(")")) {
    throw new SyntaxError(`unclosed group: ${JSON.stringify(prev)}`);
  }

  return [{ type: "group", value }, s.slice(1)] as const;
}
