// Builds a minimal mBank CSV export (cp1250, ';' delimiter, preamble before the header, Polish
// decimal comma), layout as in backend/Tests/Support/MBankCsvBuilder.cs. The expected row is
// authored by the test, never read back from the parser.

// Node can decode but not encode cp1250; the fixture only needs these characters beyond ASCII.
const CP1250: Record<string, number> = { 'ę': 0xea, 'ł': 0xb3, 'ó': 0xf3, 'ń': 0xf1 };

function encodeCp1250(text: string): Buffer {
  return Buffer.from(
    [...text].map((ch) => {
      const code = ch.charCodeAt(0);
      if (code < 0x80) return code;
      const mapped = CP1250[ch];
      if (mapped === undefined) throw new Error(`No cp1250 mapping for "${ch}" in the E2E CSV fixture`);
      return mapped;
    }),
  );
}

export function buildMBankCsv(row: { date: string; title: string; amount: number }): Buffer {
  const amount = row.amount.toFixed(2).replace('.', ',');
  const lines = [
    'mBank S.A.;;;;;;;;',
    'Elektroniczne zestawienie operacji;;;;;;;;',
    'Klient: Jan Testowy;;;;;;;;',
    "Numer rachunku: '00000000000000000000000000';;;;;;;;",
    'Waluta: PLN;;;;;;;;',
    '#Data księgowania;#Data operacji;#Opis operacji;#Tytuł;#Nadawca/Odbiorca;#Numer konta;#Kwota;#Saldo po operacji;',
    `${row.date};${row.date};TEST OPERATION;"${row.title}";"";'';${amount};0,00;`,
    '',
    '#Saldo końcowe;0,00 PLN;;;;;;;',
  ];
  return encodeCp1250(lines.join('\r\n') + '\r\n');
}
