import { describe, expect, it } from 'vitest';
import {
  inlineText,
  parseInline,
  parseMarkdown,
  splitRow,
  type Block,
} from './markdown';

// All texts here are made up.

describe('parseMarkdown', () => {
  it('reads headings, paragraphs with line breaks, a rule and code', () => {
    const blocks = parseMarkdown(
      '# Tarea\n\nLeer el texto\ny contar.\n\n---\n\n```\nuno | dos\n```\n'
    );
    expect(blocks.map((b) => b.t)).toEqual([
      'heading',
      'paragraph',
      'hr',
      'code',
    ]);
    expect(blocks[0]).toMatchObject({ t: 'heading', level: 1 });
    expect(
      (blocks[1] as Extract<Block, { t: 'paragraph' }>).c.map((n) => n.t)
    ).toEqual(['text', 'br', 'text']);
    expect(blocks[3]).toEqual({ t: 'code', lang: '', v: 'uno | dos' });
  });

  it('keeps the front matter apart', () => {
    const blocks = parseMarkdown('---\ntags: tarea\n---\n# Uno\n');
    expect(blocks[0]).toEqual({ t: 'frontmatter', v: 'tags: tarea' });
    expect(blocks[1].t).toBe('heading');
  });

  it('reads nested lists, numbered lists and checkboxes', () => {
    const blocks = parseMarkdown(
      '- uno\n  - dos\n  - tres\n- cuatro\n\n3. a\n4. b\n\n- [ ] hacer\n- [x] hecho\n'
    );
    expect(blocks.map((b) => b.t)).toEqual(['list', 'list', 'list']);
    const [ul, ol, tasks] = blocks as Extract<Block, { t: 'list' }>[];
    expect(ul.ordered).toBe(false);
    expect(ul.items).toHaveLength(2);
    expect(ul.items[0].c.map((b) => b.t)).toEqual(['paragraph', 'list']);
    expect(
      (ul.items[0].c[1] as Extract<Block, { t: 'list' }>).items
    ).toHaveLength(2);
    expect(ol).toMatchObject({ ordered: true, start: 3 });
    expect(ol.items).toHaveLength(2);
    expect(tasks.items.map((i) => i.task)).toEqual([false, true]);
  });

  it('a list item goes on over a blank line only when what follows is indented under it', () => {
    const blocks = parseMarkdown('1. uno\n\n   más de uno\n2. dos\n\nfin\n');
    expect(blocks.map((b) => b.t)).toEqual(['list', 'paragraph']);
    const list = blocks[0] as Extract<Block, { t: 'list' }>;
    expect(list.items).toHaveLength(2);
    expect(list.items[0].c.map((b) => b.t)).toEqual(['paragraph', 'paragraph']);
  });

  it('reads a table with alignment, escaped pipes and wiki links with a label', () => {
    const blocks = parseMarkdown(
      '| es | ru |\n|:---|---:|\n| casa | дом |\n| [[nota\\|la nota]] | a \\| b |\n'
    );
    const table = blocks[0] as Extract<Block, { t: 'table' }>;
    expect(table.t).toBe('table');
    expect(table.align).toEqual(['left', 'right']);
    expect(table.rows).toHaveLength(2);
    expect(table.rows[1][0]).toEqual([
      { t: 'wiki', target: 'nota', label: 'la nota' },
    ]);
    expect(inlineText(table.rows[1][1])).toBe('a | b');
  });

  it('reads quotes and callouts', () => {
    const blocks = parseMarkdown('> dicho\n\n> [!tip] Ojo\n> mira esto\n');
    expect(blocks[0]).toMatchObject({ t: 'quote', callout: null });
    expect(blocks[1]).toMatchObject({ t: 'quote', callout: 'tip' });
    expect(
      inlineText((blocks[1] as Extract<Block, { t: 'quote' }>).title!)
    ).toBe('Ojo');
  });
});

describe('parseInline', () => {
  it('reads bold, italic, struck, highlighted text and code', () => {
    expect(
      parseInline('**muy** *bien* ~~mal~~ ==ojo== `x_y`').map((n) => n.t)
    ).toEqual([
      'strong',
      'text',
      'em',
      'text',
      'del',
      'text',
      'mark',
      'text',
      'code',
    ]);
  });

  it('leaves underscores inside a word alone', () => {
    expect(parseInline('tarea_escucha_3')).toEqual([
      { t: 'text', v: 'tarea_escucha_3' },
    ]);
    expect(parseInline('es _muy_ bueno').map((n) => n.t)).toEqual([
      'text',
      'em',
      'text',
    ]);
  });

  it('reads links, embeds and wiki links', () => {
    expect(
      parseInline('[web](https://example.org) y https://example.org/a.')
    ).toEqual([
      { t: 'link', href: 'https://example.org', c: [{ t: 'text', v: 'web' }] },
      { t: 'text', v: ' y ' },
      {
        t: 'link',
        href: 'https://example.org/a',
        c: [{ t: 'text', v: 'https://example.org/a' }],
      },
      { t: 'text', v: '.' },
    ]);
    expect(parseInline('![[pizarra.png]]')).toEqual([
      { t: 'embed', name: 'pizarra.png', label: '' },
    ]);
    expect(parseInline('![foto](media/pizarra%201.png)')).toEqual([
      { t: 'embed', name: 'pizarra 1.png', label: 'foto' },
    ]);
    expect(parseInline('[[Otra nota#Parte]]')).toEqual([
      { t: 'wiki', target: 'Otra nota', label: 'Otra nota › Parte' },
    ]);
  });

  it('never keeps a tag of the note: a break stays a break, other tags are dropped', () => {
    expect(
      parseInline('uno<br>dos <span style="color:red">tres</span>')
    ).toEqual([
      { t: 'text', v: 'uno' },
      { t: 'br' },
      { t: 'text', v: 'dos tres' },
    ]);
    expect(parseInline('<script>alert(1)</script>')).toEqual([
      { t: 'text', v: 'alert(1)' },
    ]);
  });

  it('an escaped mark is text', () => {
    expect(parseInline('\\*no\\* \\[[x]]')).toEqual([
      { t: 'text', v: '*no* [[x]]' },
    ]);
  });
});

describe('splitRow', () => {
  it('splits at pipes outside code and wiki links', () => {
    expect(splitRow('| a | `b|c` | [[d|e]] |')).toEqual([
      'a',
      '`b|c`',
      '[[d|e]]',
    ]);
  });
});
