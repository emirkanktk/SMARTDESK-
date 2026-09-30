import { formatReply } from './reply-format';

describe('formatReply', () => {
  it('keeps a plain answer as one paragraph', () => {
    expect(formatReply('Room A is free at 10:00.')).toEqual([
      { kind: 'paragraph', parts: [{ text: 'Room A is free at 10:00.', strong: false }] },
    ]);
  });

  it('turns lines starting with a dash into a list between paragraphs', () => {
    const blocks = formatReply(
      'Two rooms are free:\n- Room A, 2nd floor, 8 people\n* Room B, 3rd floor, 4 people\n\nWhich one?',
    );

    expect(blocks).toEqual([
      { kind: 'paragraph', parts: [{ text: 'Two rooms are free:', strong: false }] },
      {
        kind: 'list',
        items: [
          [{ text: 'Room A, 2nd floor, 8 people', strong: false }],
          [{ text: 'Room B, 3rd floor, 4 people', strong: false }],
        ],
      },
      { kind: 'paragraph', parts: [{ text: 'Which one?', strong: false }] },
    ]);
  });

  it('keeps line breaks inside a paragraph and splits paragraphs at blank lines', () => {
    const blocks = formatReply('First line\r\nsecond line\n\n\nNext paragraph');

    expect(blocks.map((b) => b.kind === 'paragraph' && b.parts[0].text)).toEqual([
      'First line\nsecond line',
      'Next paragraph',
    ]);
  });

  it('shows **bold** as bold, never as markup, and leaves a lone ** alone', () => {
    expect(formatReply('Book **Room A** now, **not <b>this</b>**; 2 ** 3')).toEqual([
      {
        kind: 'paragraph',
        parts: [
          { text: 'Book ', strong: false },
          { text: 'Room A', strong: true },
          { text: ' now, ', strong: false },
          { text: 'not <b>this</b>', strong: true },
          { text: '; 2 ** 3', strong: false },
        ],
      },
    ]);
  });

  it('gives nothing for an empty reply', () => {
    expect(formatReply('  \n ')).toEqual([]);
  });
});
