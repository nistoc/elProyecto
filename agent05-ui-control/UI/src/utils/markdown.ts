/**
 * Markdown of the owner's notes vault (Obsidian) as a tree the app draws itself: no HTML of a note is ever put
 * into the page, a tag of a note is dropped and its text kept. What the notes use is read: headings, lists
 * (nested, numbered, with checkboxes), tables, quotes and callouts, code, a rule, bold / italic / struck /
 * highlighted text, links, wiki links `[[note|label]]`, embeds `![[picture.png]]` and the front matter.
 * A single line break inside a paragraph is a break, as the vault shows it.
 */

export type Inline =
  | { t: 'text'; v: string }
  | { t: 'strong' | 'em' | 'del' | 'mark'; c: Inline[] }
  | { t: 'code'; v: string }
  | { t: 'link'; href: string; c: Inline[] }
  | { t: 'wiki'; target: string; label: string }
  | { t: 'embed'; name: string; label: string }
  | { t: 'br' };

export type Align = 'left' | 'right' | 'center' | null;

export interface ListItem {
  /** null — a plain item; true / false — a checkbox, ticked or not. */
  task: boolean | null;
  c: Block[];
}

export type Block =
  | { t: 'heading'; level: number; c: Inline[] }
  | { t: 'paragraph'; c: Inline[] }
  | { t: 'hr' }
  | { t: 'code'; lang: string; v: string }
  | { t: 'quote'; callout: string | null; title: Inline[] | null; c: Block[] }
  | { t: 'list'; ordered: boolean; start: number; items: ListItem[] }
  | { t: 'table'; align: Align[]; head: Inline[][]; rows: Inline[][][] }
  | { t: 'frontmatter'; v: string };

const FENCE = /^\s{0,3}(`{3,}|~{3,})\s*([^`\s]*)/;
const HEADING = /^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$/;
const HR = /^\s{0,3}([-*_])(?:\s*\1){2,}\s*$/;
const QUOTE = /^\s{0,3}>/;
const ITEM = /^(\s*)([-*+]|\d{1,9}[.)])\s+(.*)$/;
const TABLE_RULE = /^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$/;
const CALLOUT = /^\[!([A-Za-z-]+)\][+-]?\s*(.*)$/;

/** The leading spaces of a line, a tab as four. */
function indentOf(line: string): number {
  let n = 0;
  for (const ch of line) {
    if (ch === ' ') n += 1;
    else if (ch === '\t') n += 4;
    else break;
  }
  return n;
}

/** The line without its first `n` columns of indentation. */
function dedent(line: string, n: number): string {
  let cols = 0;
  let i = 0;
  while (i < line.length && cols < n && (line[i] === ' ' || line[i] === '\t')) {
    cols += line[i] === '\t' ? 4 : 1;
    i += 1;
  }
  return line.slice(i);
}

function isTableStart(lines: string[], i: number): boolean {
  return (
    i + 1 < lines.length &&
    lines[i].includes('|') &&
    TABLE_RULE.test(lines[i + 1]) &&
    lines[i + 1].includes('-')
  );
}

/** A line that begins a block of its own, so a paragraph stops before it. */
function startsBlock(lines: string[], i: number): boolean {
  const line = lines[i];
  return (
    FENCE.test(line) ||
    HEADING.test(line) ||
    HR.test(line) ||
    QUOTE.test(line) ||
    ITEM.test(line) ||
    isTableStart(lines, i)
  );
}

/** Cells of a table row: split at a `|` that is not escaped, not in code and not in a wiki link. */
export function splitRow(line: string): string[] {
  let s = line.trim();
  if (s.startsWith('|')) s = s.slice(1);
  if (s.endsWith('|') && !s.endsWith('\\|')) s = s.slice(0, -1);
  const cells: string[] = [];
  let cell = '';
  let code = false;
  let wiki = 0;
  for (let i = 0; i < s.length; i += 1) {
    const ch = s[i];
    if (ch === '\\' && s[i + 1] === '|') {
      cell += '|';
      i += 1;
    } else if (ch === '`') {
      code = !code;
      cell += ch;
    } else if (!code && ch === '[' && s[i + 1] === '[') {
      wiki += 1;
      cell += '[[';
      i += 1;
    } else if (!code && ch === ']' && s[i + 1] === ']' && wiki > 0) {
      wiki -= 1;
      cell += ']]';
      i += 1;
    } else if (ch === '|' && !code && wiki === 0) {
      cells.push(cell.trim());
      cell = '';
    } else {
      cell += ch;
    }
  }
  cells.push(cell.trim());
  return cells;
}

function alignOf(rule: string): Align {
  const r = rule.trim();
  const left = r.startsWith(':');
  const right = r.endsWith(':');
  return left && right ? 'center' : right ? 'right' : left ? 'left' : null;
}

export function parseMarkdown(source: string): Block[] {
  const lines = source.replace(/\r\n?/g, '\n').split('\n');
  const blocks: Block[] = [];
  let start = 0;
  if (lines[0]?.trim() === '---') {
    const end = lines.findIndex(
      (l, i) => i > 0 && i < 200 && l.trim() === '---'
    );
    if (end > 0) {
      blocks.push({ t: 'frontmatter', v: lines.slice(1, end).join('\n') });
      start = end + 1;
    }
  }
  blocks.push(...parseBlocks(lines.slice(start)));
  return blocks;
}

function parseBlocks(lines: string[]): Block[] {
  const blocks: Block[] = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (line.trim() === '') {
      i += 1;
      continue;
    }

    const fence = FENCE.exec(line);
    if (fence) {
      const mark = fence[1];
      const body: string[] = [];
      i += 1;
      while (i < lines.length && !lines[i].trim().startsWith(mark)) {
        body.push(lines[i]);
        i += 1;
      }
      i += 1;
      blocks.push({ t: 'code', lang: fence[2] ?? '', v: body.join('\n') });
      continue;
    }

    const heading = HEADING.exec(line);
    if (heading) {
      blocks.push({
        t: 'heading',
        level: heading[1].length,
        c: parseInline(heading[2]),
      });
      i += 1;
      continue;
    }

    if (HR.test(line)) {
      blocks.push({ t: 'hr' });
      i += 1;
      continue;
    }

    if (isTableStart(lines, i)) {
      const head = splitRow(lines[i]);
      const align = splitRow(lines[i + 1]).map(alignOf);
      const rows: Inline[][][] = [];
      i += 2;
      while (
        i < lines.length &&
        lines[i].trim() !== '' &&
        lines[i].includes('|')
      ) {
        rows.push(splitRow(lines[i]).map(parseInline));
        i += 1;
      }
      blocks.push({ t: 'table', align, head: head.map(parseInline), rows });
      continue;
    }

    if (QUOTE.test(line)) {
      const inner: string[] = [];
      while (i < lines.length && QUOTE.test(lines[i])) {
        inner.push(lines[i].replace(/^\s{0,3}>\s?/, ''));
        i += 1;
      }
      const callout = CALLOUT.exec(inner[0] ?? '');
      if (callout) {
        const title = callout[2].trim();
        blocks.push({
          t: 'quote',
          callout: callout[1].toLowerCase(),
          title: title ? parseInline(title) : null,
          c: parseBlocks(inner.slice(1)),
        });
      } else {
        blocks.push({
          t: 'quote',
          callout: null,
          title: null,
          c: parseBlocks(inner),
        });
      }
      continue;
    }

    const item = ITEM.exec(line);
    if (item) {
      const [list, next] = parseList(lines, i);
      blocks.push(list);
      i = next;
      continue;
    }

    const para: string[] = [line.trim()];
    i += 1;
    while (
      i < lines.length &&
      lines[i].trim() !== '' &&
      !startsBlock(lines, i)
    ) {
      para.push(lines[i].trim());
      i += 1;
    }
    blocks.push({ t: 'paragraph', c: parseInline(para.join('\n')) });
  }
  return blocks;
}

/** A list from line `i`: its items at one indentation, with what is indented under each. */
function parseList(lines: string[], i: number): [Block, number] {
  const first = ITEM.exec(lines[i])!;
  const indent = indentOf(first[1]);
  const ordered = /\d/.test(first[2]);
  const start = ordered ? parseInt(first[2], 10) : 1;
  const items: ListItem[] = [];
  while (i < lines.length) {
    const m = ITEM.exec(lines[i]);
    if (!m || indentOf(m[1]) !== indent || /\d/.test(m[2]) !== ordered) break;
    // What lies under the item is cut to the column its own text starts at, so a nested list keeps its depth.
    const contentAt = indent + m[2].length + 1;
    const body: string[] = [m[3]];
    i += 1;
    while (i < lines.length) {
      const l = lines[i];
      if (l.trim() === '') {
        // A blank line ends the item unless what follows is indented under it.
        const next = lines.slice(i + 1).find((x) => x.trim() !== '');
        if (next !== undefined && indentOf(next) > indent) {
          body.push('');
          i += 1;
          continue;
        }
        break;
      }
      if (indentOf(l) > indent) {
        body.push(dedent(l, Math.min(contentAt, indentOf(l))));
        i += 1;
        continue;
      }
      // A line at the item's own depth that begins nothing goes on with the item's text.
      if (!startsBlock(lines, i) && indentOf(l) === indent && !ITEM.test(l)) {
        body.push(l.trim());
        i += 1;
        continue;
      }
      break;
    }
    let task: boolean | null = null;
    const box = /^\[([ xX])\]\s+/.exec(body[0]);
    if (box) {
      task = box[1] !== ' ';
      body[0] = body[0].slice(box[0].length);
    }
    items.push({ task, c: parseBlocks(body) });
    while (i < lines.length && lines[i].trim() === '') {
      const next = lines.slice(i + 1).find((x) => x.trim() !== '');
      const nm = next === undefined ? null : ITEM.exec(next);
      if (nm && indentOf(nm[1]) === indent && /\d/.test(nm[2]) === ordered)
        i += 1;
      else break;
    }
  }
  return [{ t: 'list', ordered, start, items }, i];
}

const INLINE: { re: RegExp; make: (m: RegExpExecArray) => Inline | null }[] = [
  {
    re: /\\([\\`*_{}[\]()#+\-.!|~=<>])/y,
    make: (m) => ({ t: 'text', v: m[1] }),
  },
  { re: /`([^`]+)`/y, make: (m) => ({ t: 'code', v: m[1] }) },
  {
    re: /!\[\[([^\]|]+?)(?:\|([^\]]*))?\]\]/y,
    make: (m) => ({
      t: 'embed',
      name: m[1].split('#')[0].trim(),
      label: (m[2] ?? '').trim(),
    }),
  },
  {
    re: /\[\[([^\]|]*?)(?:\|([^\]]*))?\]\]/y,
    make: (m) => {
      const target = m[1].split('#')[0].trim();
      const label = (m[2] ?? '').trim() || m[1].replace('#', ' › ').trim();
      return label ? { t: 'wiki', target, label } : null;
    },
  },
  {
    re: /!\[([^\]]*)\]\(<?([^)\s>]+)>?(?:\s+"[^"]*")?\)/y,
    make: (m) => {
      const href = m[2];
      if (/^https?:\/\//i.test(href))
        return { t: 'link', href, c: [{ t: 'text', v: m[1] || href }] };
      let name = href.split(/[\\/]/).pop() ?? href;
      try {
        name = decodeURIComponent(name);
      } catch {
        // a name that is not percent-encoded stays as it is
      }
      return { t: 'embed', name, label: m[1] };
    },
  },
  {
    re: /\[([^\]]+)\]\(<?([^)\s>]+)>?(?:\s+"[^"]*")?\)/y,
    make: (m) => ({ t: 'link', href: m[2], c: parseInline(m[1]) }),
  },
  {
    re: /https?:\/\/[^\s<>()[\]]*[^\s<>()[\].,;:!?'"]/y,
    make: (m) => ({ t: 'link', href: m[0], c: [{ t: 'text', v: m[0] }] }),
  },
  {
    re: /\*\*(?=\S)([\s\S]*?\S)\*\*/y,
    make: (m) => ({ t: 'strong', c: parseInline(m[1]) }),
  },
  {
    re: /__(?=\S)([\s\S]*?\S)__/y,
    make: (m) => ({ t: 'strong', c: parseInline(m[1]) }),
  },
  {
    re: /~~(?=\S)([\s\S]*?\S)~~/y,
    make: (m) => ({ t: 'del', c: parseInline(m[1]) }),
  },
  {
    re: /==(?=\S)([\s\S]*?\S)==/y,
    make: (m) => ({ t: 'mark', c: parseInline(m[1]) }),
  },
  {
    re: /\*(?=[^\s*])([^*]*?[^\s*])\*/y,
    make: (m) => ({ t: 'em', c: parseInline(m[1]) }),
  },
  { re: /<br\s*\/?>/iy, make: () => ({ t: 'br' }) },
  { re: /<\/?[A-Za-z][^<>]*>/y, make: () => null },
];

/** `_x_` is italic only between words, never inside one (snake_case names stay as they are). */
const UNDERSCORE_EM = /_(?=[^\s_])([^_]*?[^\s_])_(?![\p{L}\p{N}])/uy;

export function parseInline(source: string): Inline[] {
  const out: Inline[] = [];
  let text = '';
  const flush = () => {
    if (text) out.push({ t: 'text', v: text });
    text = '';
  };
  let i = 0;
  outer: while (i < source.length) {
    const ch = source[i];
    if (ch === '\n') {
      flush();
      out.push({ t: 'br' });
      i += 1;
      continue;
    }
    if ('\\`![h*_~=<'.includes(ch)) {
      for (const { re, make } of INLINE) {
        re.lastIndex = i;
        const m = re.exec(source);
        if (!m) continue;
        if (
          re.source.startsWith('https?') &&
          i > 0 &&
          /[\p{L}\p{N}]/u.test(source[i - 1])
        )
          continue;
        const node = make(m);
        // A dropped tag leaves the text around it in one piece.
        if (node?.t === 'text') text += node.v;
        else if (node) {
          flush();
          out.push(node);
        }
        i += m[0].length;
        continue outer;
      }
      if (ch === '_' && (i === 0 || !/[\p{L}\p{N}]/u.test(source[i - 1]))) {
        UNDERSCORE_EM.lastIndex = i;
        const m = UNDERSCORE_EM.exec(source);
        if (m) {
          flush();
          out.push({ t: 'em', c: parseInline(m[1]) });
          i += m[0].length;
          continue;
        }
      }
    }
    if (ch === '[') {
      for (const { re, make } of INLINE) {
        if (!re.source.startsWith('\\[')) continue;
        re.lastIndex = i;
        const m = re.exec(source);
        if (!m) continue;
        const node = make(m);
        if (node) {
          flush();
          out.push(node);
        }
        i += m[0].length;
        continue outer;
      }
    }
    text += ch;
    i += 1;
  }
  flush();
  return out;
}

/** The plain text of inline nodes (a title, a search). */
export function inlineText(nodes: Inline[]): string {
  return nodes
    .map((n) => {
      switch (n.t) {
        case 'text':
        case 'code':
          return n.v;
        case 'br':
          return ' ';
        case 'wiki':
        case 'embed':
          return n.label || (n.t === 'wiki' ? n.target : n.name);
        default:
          return inlineText(n.c);
      }
    })
    .join('');
}
