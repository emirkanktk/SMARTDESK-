/** A run of a reply's text; bold where the model wrapped it in ** (it's asked not to). */
export interface ReplyText {
  text: string;
  strong: boolean;
}

export type ReplyBlock =
  { kind: 'paragraph'; parts: ReplyText[] } | { kind: 'list'; items: ReplyText[][] };

const LIST_ITEM = /^\s*[-*•]\s+(.*)$/;
const BOLD = /\*\*(.+?)\*\*/g;

/**
 * The assistant's reply as paragraphs and lists, to render as text - never as HTML. Lines
 * that start with "- " (or "* ", "• ") make a list, a blank line ends a paragraph, and other
 * line breaks are kept.
 */
export function formatReply(reply: string): ReplyBlock[] {
  const blocks: ReplyBlock[] = [];
  let paragraph: string[] = [];
  let list: string[] = [];

  const endParagraph = () => {
    if (paragraph.length > 0) {
      blocks.push({ kind: 'paragraph', parts: inline(paragraph.join('\n')) });
      paragraph = [];
    }
  };
  const endList = () => {
    if (list.length > 0) {
      blocks.push({ kind: 'list', items: list.map(inline) });
      list = [];
    }
  };

  for (const line of reply.replace(/\r\n?/g, '\n').split('\n')) {
    const item = LIST_ITEM.exec(line);
    if (item) {
      endParagraph();
      list.push(item[1].trim());
    } else if (line.trim() === '') {
      endParagraph();
      endList();
    } else {
      endList();
      paragraph.push(line.trim());
    }
  }
  endParagraph();
  endList();
  return blocks;
}

function inline(text: string): ReplyText[] {
  const parts: ReplyText[] = [];
  let last = 0;
  for (const match of text.matchAll(BOLD)) {
    if (match.index > last) {
      parts.push({ text: text.slice(last, match.index), strong: false });
    }
    parts.push({ text: match[1], strong: true });
    last = match.index + match[0].length;
  }
  if (last < text.length) {
    parts.push({ text: text.slice(last), strong: false });
  }
  return parts;
}
